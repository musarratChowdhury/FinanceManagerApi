using System.Net;
using System.Text;
using System.Text.Json;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Errors;
using FinanceManagerApi.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinanceManagerApi.Moq;

public sealed class GeminiReceiptScanServiceTests
{
    private const string ReceiptJson =
        """{"merchant":"Green Leaf Pharmacy","date":"2026-10-22","currency":"USD","items":[{"name":"Milk","quantity":1,"unitPrice":5.5,"amount":5.5},{"name":"Bread","quantity":1,"unitPrice":7,"amount":7}],"subtotal":12.5,"discount":0,"total":12.5,"rawTotalText":"TOTAL $12.50"}""";

    /// <summary>A Gemini success envelope whose single text part holds the receipt JSON.</summary>
    private static readonly string SuccessBody = JsonSerializer.Serialize(new
    {
        candidates = new[]
        {
            new { content = new { parts = new[] { new { text = ReceiptJson } } } }
        }
    });

    private const string NotFoundBody =
        """{"error":{"code":404,"message":"This model models/gemini-2.5-flash is no longer available to new users. Please update your code to use models/gemini-3.8-flash for the latest features and improvements.","status":"NOT_FOUND"}}""";

    private const string BusyBody =
        """{"error":{"code":503,"message":"This model is currently experiencing high demand. Spikes in demand are usually temporary. Please try again later.","status":"UNAVAILABLE"}}""";

    private const string QuotaBody =
        """{"error":{"code":429,"message":"You exceeded your current quota.","status":"RESOURCE_EXHAUSTED","details":[{"retryDelay":"23s"}]}}""";

    private const string TextOnlyBody =
        """{"error":{"code":400,"message":"This model does not support image input.","status":"INVALID_ARGUMENT"}}""";

    private const string ModelsBody =
        """
        {
          "models": [
            {
              "name": "models/gemini-2.5-pro-preview-tts",
              "version": "gemini-2.5-pro-preview-tts-2025-05-19",
              "displayName": "Gemini 2.5 Pro Preview TTS",
              "inputTokenLimit": 8192,
              "outputTokenLimit": 16384,
              "supportedGenerationMethods": ["countTokens", "generateContent", "batchGenerateContent"]
            },
            {
              "name": "models/gemma-4-26b-a4b-it",
              "displayName": "Gemma 4",
              "inputTokenLimit": 32000,
              "supportedGenerationMethods": ["generateContent"]
            },
            {
              "name": "models/text-embedding-004",
              "displayName": "Embedding",
              "supportedGenerationMethods": ["embedContent", "countTokens"]
            }
          ]
        }
        """;

    /* --------------------------------------------------------------- scanning */

    [Theory]
    [InlineData("306571b207386a00f4a1d293ec1e8b3d.jpg")]
    [InlineData("4b03dde1d159048fb76f60c109543c79.jpg")]
    public async Task ScanAsync_ShouldReturnStructuredReceipt(string sampleName)
    {
        var handler = new GeminiHandler(_ => Success);
        var service = Build(handler);

        await using var image = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", sampleName));
        var result = await service.ScanAsync(image, "image/jpeg");

        Assert.Equal("Green Leaf Pharmacy", result.Merchant);
        Assert.Equal(12.50m, result.Total);
        Assert.Equal(2, result.Items.Count);
        Assert.True(result.ItemsMatchTotal);
        Assert.False(result.NeedsReview);
        Assert.Equal("gemma-4-26b-a4b-it", result.ModelUsed);
        Assert.Contains("inline_data", handler.BodyOf("/v1beta/models/gemma-4-26b-a4b-it:generateContent"));
        Assert.Equal("test-key", handler.ApiKey);
        Assert.Equal(
            "/v1beta/models/gemma-4-26b-a4b-it:generateContent",
            handler.Uris.Single().AbsolutePath);
    }

    [Fact]
    public async Task ScanAsync_ShouldUseTheRequestedModel()
    {
        var handler = new GeminiHandler(_ => Success);
        var service = Build(handler);

        var result = await service.ScanAsync(Image(), "image/jpeg", "models/gemini-3.8-flash");

        Assert.Equal("gemini-3.8-flash", result.ModelUsed);
        Assert.Equal(
            "/v1beta/models/gemini-3.8-flash:generateContent",
            handler.Uris.Single().AbsolutePath);
    }

    [Fact]
    public async Task ScanAsync_ShouldNeverPutAnInvalidModelNameInTheUrl()
    {
        var handler = new GeminiHandler(_ => Success);
        var service = Build(handler);

        // The controller rejects this with a 400 first; the service must still not
        // build a path out of it if a caller sneaks one in.
        await service.ScanAsync(Image(), "image/jpeg", "../../etc/passwd");

        Assert.All(handler.Uris, uri => Assert.StartsWith("/v1beta/models/gemma-4-26b-a4b-it", uri.AbsolutePath));
    }

    [Fact]
    public async Task ScanAsync_ShouldFallBackToTheDefaultWhenTheRequestedModelIsRetired()
    {
        var handler = new GeminiHandler(model =>
            model == "gemini-2.5-flash" ? NotFound : Success);
        var service = Build(handler);

        var result = await service.ScanAsync(Image(), "image/jpeg", "gemini-2.5-flash");

        Assert.Equal("gemma-4-26b-a4b-it", result.ModelUsed);
        Assert.Equal(
            new[]
            {
                "/v1beta/models/gemini-2.5-flash:generateContent",
                "/v1beta/models/gemma-4-26b-a4b-it:generateContent"
            },
            handler.Uris.Select(uri => uri.AbsolutePath));
    }

    [Fact]
    public async Task ScanAsync_ShouldReportRetirementAndTheSuggestedReplacement()
    {
        var handler = new GeminiHandler(_ => NotFound);
        var service = Build(handler);

        var failure = await Assert.ThrowsAsync<GeminiScanException>(() =>
            service.ScanAsync(Image(), "image/jpeg", "gemini-2.5-flash", strict: true));

        var attempt = Assert.Single(failure.Attempts);
        Assert.Equal(GeminiOutcomes.NotFound, attempt.Outcome);
        Assert.False(attempt.Retryable);
        Assert.Equal(404, attempt.HttpStatus);
        Assert.Equal("NOT_FOUND", attempt.ErrorStatus);
        Assert.Equal("gemini-3.8-flash", attempt.SuggestedModel);
        Assert.Contains("no longer available", attempt.Message);
        Assert.Equal(NotFoundBody, attempt.RawResponse);
    }

    [Fact]
    public async Task ScanAsync_ShouldTreatHighDemandAsRetryable()
    {
        var handler = new GeminiHandler(_ => Busy);
        var service = Build(handler);

        var failure = await Assert.ThrowsAsync<GeminiScanException>(() =>
            service.ScanAsync(Image(), "image/jpeg", "gemini-3.8-flash", strict: true));

        var attempt = Assert.Single(failure.Attempts);
        Assert.Equal(GeminiOutcomes.Busy, attempt.Outcome);
        Assert.Equal("UNAVAILABLE", attempt.ErrorStatus);
        Assert.True(attempt.Retryable);
        Assert.Equal(BusyBody, attempt.RawResponse);
    }

    [Fact]
    public async Task ScanAsync_ShouldTreatQuotaAsRetryable()
    {
        var handler = new GeminiHandler(_ => Quota);
        var service = Build(handler);

        var failure = await Assert.ThrowsAsync<GeminiScanException>(() =>
            service.ScanAsync(Image(), "image/jpeg", "gemini-3.8-flash", strict: true));

        var attempt = Assert.Single(failure.Attempts);
        Assert.Equal(GeminiOutcomes.Quota, attempt.Outcome);
        Assert.True(attempt.Retryable);
        Assert.Equal(QuotaBody, attempt.RawResponse);
    }

    [Fact]
    public async Task ScanAsync_ShouldRecogniseATextOnlyModel()
    {
        var handler = new GeminiHandler(_ => TextOnly);
        var service = Build(handler);

        var failure = await Assert.ThrowsAsync<GeminiScanException>(() =>
            service.ScanAsync(Image(), "image/jpeg", "gemini-3.8-flash", strict: true));

        var attempt = Assert.Single(failure.Attempts);
        Assert.Equal(GeminiOutcomes.NoImageSupport, attempt.Outcome);
        Assert.False(attempt.Retryable);
    }

    [Fact]
    public async Task ScanAsync_ShouldStopTheChainOnANonRetryableFailure()
    {
        var handler = new GeminiHandler(_ => Unauthorized);
        var service = Build(handler);

        var failure = await Assert.ThrowsAsync<GeminiScanException>(() =>
            service.ScanAsync(Image(), "image/jpeg", "gemini-3.8-flash"));

        Assert.Single(failure.Attempts);
        Assert.Single(handler.Uris);
        Assert.Equal(GeminiOutcomes.AuthError, failure.Attempts[0].Outcome);
    }

    [Fact]
    public async Task ScanAsync_ShouldTimeOutAfterTheConfiguredBudget()
    {
        var handler = new GeminiHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return Success;
        });
        var service = Build(handler, new Dictionary<string, string?>
        {
            ["Gemini:AttemptTimeoutSeconds"] = "1"
        });

        var failure = await Assert.ThrowsAsync<GeminiScanException>(() =>
            service.ScanAsync(Image(), "image/jpeg", "gemini-3.8-flash", strict: true));

        var attempt = Assert.Single(failure.Attempts);
        Assert.Equal(GeminiOutcomes.Timeout, attempt.Outcome);
        Assert.True(attempt.Retryable);
        Assert.Contains("1 second", attempt.Message);
        Assert.Null(attempt.RawResponse);
    }

    [Fact]
    public void TryNormalizeModelName_ShouldStripThePrefixAndRejectPaths()
    {
        Assert.True(GeminiReceiptScanService.TryNormalizeModelName("models/gemini-3.8-flash", out var stripped));
        Assert.Equal("gemini-3.8-flash", stripped);

        Assert.False(GeminiReceiptScanService.TryNormalizeModelName("../models/x", out _));
        Assert.False(GeminiReceiptScanService.TryNormalizeModelName("a/b", out _));
        Assert.False(GeminiReceiptScanService.TryNormalizeModelName("  ", out _));
        Assert.False(GeminiReceiptScanService.TryNormalizeModelName("models/", out _));
    }

    /* ------------------------------------------------------------- catalogue */

    [Fact]
    public async Task ListModelsAsync_ShouldFlagModelsWithoutGenerateContent()
    {
        var handler = new GeminiHandler(_ => Success, ModelsBody);
        var service = Build(handler);

        var result = await service.ListModelsAsync();

        Assert.True(result.KeyConfigured);
        Assert.Equal("gemma-4-26b-a4b-it", result.DefaultModel);
        Assert.Equal(10, result.AttemptTimeoutSeconds);
        Assert.Equal(3, result.Models.Count);

        // The default sorts first.
        Assert.Equal("gemma-4-26b-a4b-it", result.Models[0].Name);

        var tts = result.Models.Single(m => m.Name == "gemini-2.5-pro-preview-tts");
        Assert.True(tts.SupportsGenerateContent);
        Assert.Contains("generateContent", tts.SupportedGenerationMethods);
        Assert.Equal("gemini-2.5-pro-preview-tts-2025-05-19", tts.Version);

        var embedding = result.Models.Single(m => m.Name == "text-embedding-004");
        Assert.False(embedding.SupportsGenerateContent);
    }

    [Fact]
    public async Task ListModelsAsync_ShouldReportAMissingKey()
    {
        var handler = new GeminiHandler(_ => Success);
        var service = Build(handler, apiKey: null);

        var result = await service.ListModelsAsync();

        Assert.False(result.KeyConfigured);
        Assert.Empty(result.Models);
        Assert.Empty(handler.Uris);
    }

    /* ----------------------------------------------------------------- probe */

    [Fact]
    public async Task ProbeAsync_ShouldSkipModelsThatCannotGenerateContent()
    {
        var handler = new GeminiHandler(
            model => model == "gemini-3.8-flash" ? Success : Busy,
            ModelsBody);
        var service = Build(handler);

        var result = await service.ProbeAsync(
            ["text-embedding-004", "gemini-3.8-flash", "gemini-2.5-pro-preview-tts"]);

        Assert.Equal("gemini-3.8-flash", result.Winner);
        Assert.True(result.KeyConfigured);

        // The catalogue fetch is the only GET; embeddings and TTS cost no calls.
        Assert.Single(handler.Uris, uri => uri.AbsolutePath.EndsWith(":generateContent"));
        Assert.Equal(
            GeminiOutcomes.NoGenerateContent,
            result.Results.Single(r => r.Model == "text-embedding-004").Outcome);
        Assert.Equal(
            GeminiOutcomes.Unsupported,
            result.Results.Single(r => r.Model == "gemini-2.5-pro-preview-tts").Outcome);
        Assert.Equal(GeminiOutcomes.Ok, result.Results.Single(r => r.Model == "gemini-3.8-flash").Outcome);
    }

    [Fact]
    public async Task ProbeAsync_ShouldReturnTheFirstModelThatAnswers()
    {
        var handler = new GeminiHandler(model => model == "gemini-2.5-flash" ? NotFound : Success);
        var service = Build(handler);

        var result = await service.ProbeAsync(["gemini-2.5-flash", "gemini-3.8-flash"]);

        Assert.Equal("gemini-3.8-flash", result.Winner);
        Assert.Equal(2, result.Results.Count);
        Assert.Equal(GeminiOutcomes.NotFound, result.Results[0].Outcome);
        Assert.Equal("gemini-3.8-flash", result.Results[0].SuggestedModel);
    }

    [Fact]
    public async Task ProbeAsync_ShouldReportNoWinnerWhenNothingWorks()
    {
        var handler = new GeminiHandler(model => model == "gemini-3.8-flash" ? Busy : NotFound);
        var service = Build(handler);

        var result = await service.ProbeAsync(["gemini-2.5-flash", "gemini-3.8-flash"]);

        Assert.Null(result.Winner);
        Assert.Equal(GeminiOutcomes.NotFound, result.Results[0].Outcome);
        Assert.Equal(GeminiOutcomes.Busy, result.Results[1].Outcome);
    }

    [Fact]
    public async Task ProbeAsync_ShouldStopTheSweepWhenTheKeyIsOutOfQuota()
    {
        var handler = new GeminiHandler(_ => Quota);
        var service = Build(handler);

        var result = await service.ProbeAsync(["gemini-a-flash", "gemini-b-flash", "gemini-c-flash"]);

        Assert.Null(result.Winner);
        Assert.Contains(result.Results, attempt => attempt.Outcome == GeminiOutcomes.Quota);
        Assert.Contains(result.Results, attempt => attempt.Outcome == GeminiOutcomes.Skipped);
    }

    [Fact]
    public async Task ProbeAsync_ShouldDefaultToTheConfiguredModel()
    {
        var handler = new GeminiHandler(_ => Success);
        var service = Build(handler);

        var result = await service.ProbeAsync();

        Assert.Equal("gemma-4-26b-a4b-it", result.Winner);
        Assert.Equal(
            "/v1beta/models/gemma-4-26b-a4b-it:generateContent",
            handler.Uris.Single(uri => uri.AbsolutePath.EndsWith(":generateContent")).AbsolutePath);
    }

    /* ---------------------------------------------------------------- helpers */

    private static HttpResponseMessage Success => Reply(HttpStatusCode.OK, SuccessBody);
    private static HttpResponseMessage NotFound => Reply(HttpStatusCode.NotFound, NotFoundBody);
    private static HttpResponseMessage Busy => Reply(HttpStatusCode.ServiceUnavailable, BusyBody);
    private static HttpResponseMessage Quota => Reply(HttpStatusCode.TooManyRequests, QuotaBody);
    private static HttpResponseMessage TextOnly => Reply(HttpStatusCode.BadRequest, TextOnlyBody);
    private static HttpResponseMessage Unauthorized => Reply(HttpStatusCode.Unauthorized,
        """{"error":{"code":401,"message":"API key not valid.","status":"UNAUTHENTICATED"}}""");

    private static HttpResponseMessage Reply(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static Stream Image() => new MemoryStream(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 });

    private static GeminiReceiptScanService Build(
        GeminiHandler handler,
        Dictionary<string, string?>? settings = null,
        string? apiKey = "test-key")
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/")
        };

        var values = new Dictionary<string, string?>
        {
            ["GEMINI_API_KEY"] = apiKey,
            ["Gemini:DefaultModel"] = "gemma-4-26b-a4b-it",
            ["Gemini:AttemptTimeoutSeconds"] = "10",
            ["Gemini:ProbeConcurrency"] = "2"
        };

        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        return new GeminiReceiptScanService(
            client,
            new ConfigurationBuilder().AddInMemoryCollection(values).Build(),
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GeminiReceiptScanService>.Instance);
    }

    /// <summary>
    /// Answers the model-list GET from <c>modelsBody</c> and each generateContent
    /// POST from <c>respond</c>, recording every URI and body it saw.
    /// </summary>
    private sealed class GeminiHandler : HttpMessageHandler
    {
        private readonly Func<string, HttpResponseMessage> _respond;
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _asyncRespond;
        private readonly string _modelsBody;

        public GeminiHandler(
            Func<string, HttpResponseMessage> respond,
            string modelsBody = ModelsBody)
        {
            _respond = respond;
            _modelsBody = modelsBody;
        }

        public GeminiHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        {
            _asyncRespond = respond;
            _respond = _ => Success;
            _modelsBody = ModelsBody;
        }

        public List<Uri> Uris { get; } = [];
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
        public string ApiKey { get; private set; } = string.Empty;

        public string BodyOf(string path) => Bodies.TryGetValue(path, out var body) ? body : string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            Uris.Add(uri);
            if (request.Headers.TryGetValues("x-goog-api-key", out var keys))
            {
                ApiKey = keys.Single();
            }

            if (request.Method == HttpMethod.Get)
            {
                return Reply(HttpStatusCode.OK, _modelsBody);
            }

            if (request.Content is not null)
            {
                Bodies[uri.AbsolutePath] = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            if (_asyncRespond is not null)
            {
                return await _asyncRespond(request, cancellationToken);
            }

            var index = uri.AbsolutePath.IndexOf("/v1beta/models/", StringComparison.Ordinal);
            var model = index < 0
                ? string.Empty
                : uri.AbsolutePath[(index + "/v1beta/models/".Length)..].Replace(":generateContent", string.Empty);

            return _respond(model);
        }
    }
}
