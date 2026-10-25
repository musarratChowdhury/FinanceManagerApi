using System.Net.Http.Json;
using System.Text.Json;
using FinanceManagerApi.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace FinanceManagerApi.Moq;

public sealed class GeminiReceiptScanLiveTests
{
    private const string GeminiBaseAddress = "https://generativelanguage.googleapis.com/";

    private readonly ITestOutputHelper Output;

    public GeminiReceiptScanLiveTests(ITestOutputHelper output)
    {
        Output = output;
    }

    [GeminiFact]
    [Trait("Category", "Integration")]
    public async Task GenerateContent_ShouldRespondToMinimalTextRequest()
    {
        var apiKey = GetApiKey();
        var model = Environment.GetEnvironmentVariable("GEMINI_TEST_MODEL") ?? "gemma-4-26b-a4b-it";

        using var client = new HttpClient
        {
            BaseAddress = new Uri(GeminiBaseAddress),
            Timeout = TimeSpan.FromSeconds(60)
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{model}:generateContent");
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            contents = new[]
            {
                new
                {
                    parts = new[]
                    {
                        new { text = "Reply with exactly: OK" }
                    }
                }
            }
        });

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.IsSuccessStatusCode,
            $"Gemini returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}");

        using var document = JsonDocument.Parse(body);
        var text = document.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        Assert.Equal("OK", text?.Trim());
    }

    [GeminiTheory]
    [InlineData("306571b207386a00f4a1d293ec1e8b3d.jpg")]
    [InlineData("4b03dde1d159048fb76f60c109543c79.jpg")]
    [Trait("Category", "Integration")]
    public async Task ReceiptScan_ShouldProcessSampleReceipt(string sampleName)
    {
        var apiKey = GetApiKey();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(GeminiBaseAddress),
            Timeout = TimeSpan.FromSeconds(90)
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = apiKey
            })
            .Build();
        var service = new GeminiReceiptScanService(
            client,
            configuration,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GeminiReceiptScanService>.Instance);

        await using var image = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", sampleName));
        var result = await service.ScanAsync(image, "image/jpeg");

        Assert.False(string.IsNullOrWhiteSpace(result.Merchant));
        Assert.NotEmpty(result.Items);
        Assert.NotNull(result.Total);
    }

    [GeminiFact]
    [Trait("Category", "Integration")]
    public async Task ListModels_ShouldReturnTheLiveCatalogue()
    {
        var apiKey = GetApiKey();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(GeminiBaseAddress),
            Timeout = TimeSpan.FromSeconds(30)
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = apiKey
            })
            .Build();
        var service = new GeminiReceiptScanService(
            client,
            configuration,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GeminiReceiptScanService>.Instance);

        var result = await service.ListModelsAsync();

        Assert.True(result.KeyConfigured);
        Assert.NotEmpty(result.Models);
        Assert.Contains(result.Models, model => model.SupportsGenerateContent);
        Assert.All(result.Models, model => Assert.DoesNotContain('/', model.Name));
    }

    /// <summary>
    /// The availability sweep, against the live key. Prints the per-model verdicts
    /// so a deprecation surfaces as a readable table in the test output.
    /// </summary>
    [GeminiFact]
    [Trait("Category", "Integration")]
    public async Task Probe_ShouldFindAWorkingModel()
    {
        var apiKey = GetApiKey();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(GeminiBaseAddress),
            Timeout = TimeSpan.FromSeconds(90)
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["GEMINI_API_KEY"] = apiKey,
                ["Gemini:AttemptTimeoutSeconds"] = "20"
            })
            .Build();
        var service = new GeminiReceiptScanService(
            client,
            configuration,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<GeminiReceiptScanService>.Instance);

        var result = await service.ProbeAsync(
            [service.DefaultModel, "gemini-3.8-flash", "gemini-2.5-flash"],
            fresh: true);

        foreach (var attempt in result.Results)
        {
            Output.WriteLine(
                "{0,-28} {1,-20} {2,-4} {3,6}ms  {4}",
                attempt.Model,
                attempt.Outcome,
                attempt.HttpStatus?.ToString() ?? "-",
                attempt.ElapsedMs,
                attempt.Message ?? string.Empty);
        }

        Assert.NotNull(result.Winner);
    }

    private static string GetApiKey()
    {
        return Environment.GetEnvironmentVariable("GEMINI_API_KEY")!;
    }
}

internal sealed class GeminiFactAttribute : FactAttribute
{
    public GeminiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
        {
            Skip = "Set GEMINI_API_KEY to run live Gemini integration tests.";
        }
    }
}

internal sealed class GeminiTheoryAttribute : TheoryAttribute
{
    public GeminiTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")))
        {
            Skip = "Set GEMINI_API_KEY to run live Gemini integration tests.";
        }
    }
}
