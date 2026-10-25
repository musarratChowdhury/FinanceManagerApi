using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Errors;
using Microsoft.Extensions.Caching.Memory;

namespace FinanceManagerApi.Services;

public interface IGeminiReceiptScanService
{
    /// <summary>Fallback used when the client has not chosen a model.</summary>
    string DefaultModel { get; }

    /// <summary>Per-model attempt budget, in seconds.</summary>
    int AttemptTimeoutSeconds { get; }

    /// <summary>
    /// Reads a receipt with <paramref name="model"/>.
    /// <paramref name="strict"/> limits the call to that one model (used by the
    /// settings screen so a fallback cannot disguise a broken model).
    /// </summary>
    Task<ReceiptScanDto> ScanAsync(
        Stream image,
        string mimeType,
        string? model = null,
        bool strict = false,
        CancellationToken cancellationToken = default);

    /// <summary>Models visible to the configured API key, unfiltered.</summary>
    Task<GeminiModelsDto> ListModelsAsync(CancellationToken cancellationToken = default);

    /// <summary>Availability sweep: which models can actually read a receipt right now.</summary>
    Task<GeminiProbeDto> ProbeAsync(
        IReadOnlyList<string>? models = null,
        bool fresh = false,
        CancellationToken cancellationToken = default);
}

public sealed class GeminiReceiptScanService : IGeminiReceiptScanService
{
    private const string FallbackDefaultModel = "gemma-4-26b-a4b-it";
    private const int FallbackAttemptTimeoutSeconds = 10;
    private const int FallbackProbeConcurrency = 4;
    private const int MaxProbeModels = 25;
    private const int MaxRawResponseChars = 4096;

    private static readonly TimeSpan ModelsCacheTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SweepCacheTtl = TimeSpan.FromMinutes(10);

    private static readonly Regex ModelNamePattern =
        new("^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);

    // "Please update your code to use models/gemini-3.8-flash ..."
    private static readonly Regex SuggestedModelPattern =
        new(@"use\s+models/([A-Za-z0-9][A-Za-z0-9._-]{0,63})", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyModelPattern =
        new(@"models/([A-Za-z0-9][A-Za-z0-9._-]{0,63})", RegexOptions.Compiled);

    // Cost heuristic only: these families cannot read a receipt, so probing them
    // would spend a request to learn nothing. The probe stays the source of truth.
    private static readonly Regex NotAReceiptReader =
        new(@"(^|[-_.])(tts|image|embedding|embeddings|aqa|imagen|veo|live)($|[-_.])",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ImageRejection =
        new(@"(image|inline_data|inline data|multimodal|vision|media)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _cache;
    private readonly ILogger<GeminiReceiptScanService> _logger;
    private readonly Lazy<byte[]?> _probeImage;

    public GeminiReceiptScanService(
        HttpClient httpClient,
        IConfiguration configuration,
        IMemoryCache cache,
        ILogger<GeminiReceiptScanService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _cache = cache;
        _logger = logger;
        _probeImage = new Lazy<byte[]?>(() => LoadProbeImage(logger));
    }

    public string DefaultModel =>
        NormalizeModelName(_configuration["Gemini:DefaultModel"]) ?? FallbackDefaultModel;

    public int AttemptTimeoutSeconds
    {
        get
        {
            var configured = _configuration.GetValue<int?>("Gemini:AttemptTimeoutSeconds");
            return configured is > 0 and <= 120 ? configured.Value : FallbackAttemptTimeoutSeconds;
        }
    }

    private int ProbeConcurrency
    {
        get
        {
            var configured = _configuration.GetValue<int?>("Gemini:ProbeConcurrency");
            return configured is > 0 and <= 8 ? configured.Value : FallbackProbeConcurrency;
        }
    }

    /// <summary>
    /// Strips a `models/` prefix and rejects anything that is not a plain model
    /// id. The name is interpolated into the request path, so this is the guard
    /// that keeps `../` and query-string tricks out of the URL.
    /// </summary>
    public static bool TryNormalizeModelName(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim();
        if (candidate.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            candidate = candidate["models/".Length..];
        }

        if (candidate.Contains("..", StringComparison.Ordinal) || !ModelNamePattern.IsMatch(candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    public async Task<ReceiptScanDto> ScanAsync(
        Stream image,
        string mimeType,
        string? model = null,
        bool strict = false,
        CancellationToken cancellationToken = default)
    {
        var requested = NormalizeModelName(model);
        var chain = BuildChain(requested, strict);

        byte[] bytes;
        using (var buffer = new MemoryStream())
        {
            await image.CopyToAsync(buffer, cancellationToken);
            bytes = buffer.ToArray();
        }

        var apiKey = GetApiKey();
        if (apiKey is null)
        {
            throw new GeminiScanException(
                "GEMINI_API_KEY is not configured on the server.",
                [
                    new GeminiAttemptDto
                    {
                        Model = chain[0],
                        Outcome = GeminiOutcomes.AuthError,
                        Message = "GEMINI_API_KEY is not configured on the server."
                    }
                ]);
        }

        var attempts = new List<GeminiAttemptDto>(chain.Count);
        foreach (var candidate in chain)
        {
            var (attempt, scan) = await CallAsync(candidate, bytes, mimeType, apiKey, cancellationToken);
            attempts.Add(attempt);

            if (attempt.Outcome == GeminiOutcomes.Ok && scan is not null)
            {
                return scan;
            }

            // A retired or text-only model only rules that model out — the next
            // candidate may still work, which is the point of the chain. auth_error
            // and quota rule out every model the key could reach, so stop there.
            if (attempt.Outcome is GeminiOutcomes.AuthError or GeminiOutcomes.Quota)
            {
                break;
            }
        }

        throw new GeminiScanException(
            chain.Count == 1
                ? $"Gemini could not read this receipt with {chain[0]}."
                : "Every Gemini model we tried failed.",
            attempts);
    }

    public async Task<GeminiModelsDto> ListModelsAsync(CancellationToken cancellationToken = default)
    {
        var defaultModel = DefaultModel;
        var cacheKey = $"gemini:models:{defaultModel}";
        if (_cache.TryGetValue(cacheKey, out GeminiModelsDto? cached) && cached is not null)
        {
            return cached;
        }

        var apiKey = GetApiKey();
        if (apiKey is null)
        {
            return new GeminiModelsDto
            {
                Models = [],
                DefaultModel = defaultModel,
                KeyConfigured = false,
                AttemptTimeoutSeconds = AttemptTimeoutSeconds
            };
        }

        var models = new List<GeminiModelDto>();
        string? pageToken = null;

        for (var page = 0; page < 5; page++)
        {
            var uri = "v1beta/models?pageSize=200" +
                      (pageToken is null ? string.Empty : $"&pageToken={Uri.EscapeDataString(pageToken)}");

            var body = await GetStringAsync(uri, apiKey, "models.list", cancellationToken);
            pageToken = ReadModelsPage(body, models);

            if (pageToken is null)
            {
                break;
            }
        }

        var sorted = models
            .OrderByDescending(m => string.Equals(m.Name, defaultModel, StringComparison.OrdinalIgnoreCase))
            .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var result = new GeminiModelsDto
        {
            Models = sorted,
            DefaultModel = defaultModel,
            KeyConfigured = true,
            AttemptTimeoutSeconds = AttemptTimeoutSeconds
        };

        _cache.Set(cacheKey, result, ModelsCacheTtl);
        return result;
    }

    public async Task<GeminiProbeDto> ProbeAsync(
        IReadOnlyList<string>? models = null,
        bool fresh = false,
        CancellationToken cancellationToken = default)
    {
        var defaultModel = DefaultModel;
        var apiKey = GetApiKey();
        var probeImage = _probeImage.Value;
        var probeMode = probeImage is null ? ProbeModes.Text : ProbeModes.Receipt;

        if (apiKey is null)
        {
            return new GeminiProbeDto
            {
                KeyConfigured = false,
                ProbeMode = probeMode,
                Results = []
            };
        }

        var candidates = BuildCandidateList(models, defaultModel);
        if (candidates.Count == 0)
        {
            return new GeminiProbeDto { KeyConfigured = true, ProbeMode = probeMode, Results = [] };
        }

        var sweepKey = "gemini:sweep:" + string.Join('|', candidates);
        if (!fresh && _cache.TryGetValue(sweepKey, out GeminiProbeDto? cachedSweep) && cachedSweep is not null)
        {
            return new GeminiProbeDto
            {
                Winner = cachedSweep.Winner,
                Results = cachedSweep.Results,
                KeyConfigured = cachedSweep.KeyConfigured,
                ProbeMode = cachedSweep.ProbeMode,
                Cached = true,
                CheckedAt = cachedSweep.CheckedAt
            };
        }

        var metadata = await TryGetModelIndexAsync(apiKey, cancellationToken);
        var results = new GeminiAttemptDto?[candidates.Count];

        // Metadata and name-family checks are free, so they run for every candidate
        // up front. Only models that could plausibly read a receipt get a request
        // and compete for a concurrency slot — and they are the only ones an early
        // stop can leave as "skipped".
        var callable = new List<int>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            var precheck = ClassifyWithoutCall(candidates[index], metadata);
            if (precheck is null)
            {
                callable.Add(index);
            }
            else
            {
                results[index] = precheck;
            }
        }

        string? winner = null;

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var gate = new SemaphoreSlim(ProbeConcurrency);

        var workers = callable.Select(async index =>
        {
            var model = candidates[index];

            try
            {
                await gate.WaitAsync(stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                if (stop.IsCancellationRequested)
                {
                    return;
                }

                if (!fresh &&
                    _cache.TryGetValue($"gemini:probe:{model}", out GeminiAttemptDto? cached) &&
                    cached is not null)
                {
                    results[index] = cached;
                    Track(cached);
                    return;
                }

                var payload = probeImage is null
                    ? TextProbePayload
                    : BuildReceiptPayload(probeImage, "image/jpeg");
                var (attempt, _) = await SendAsync(model, payload, apiKey, stop.Token);
                results[index] = attempt;

                if (CacheLifetime(attempt.Outcome) is { } lifetime)
                {
                    _cache.Set($"gemini:probe:{model}", attempt, lifetime);
                }

                Track(attempt);
            }
            catch (OperationCanceledException)
            {
                // The sweep stopped early: leave this entry as skipped.
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(workers);

        var ordered = new List<GeminiAttemptDto>(candidates.Count);
        for (var index = 0; index < candidates.Count; index++)
        {
            ordered.Add(results[index] ?? new GeminiAttemptDto
            {
                Model = candidates[index],
                Outcome = GeminiOutcomes.Skipped,
                Message = stop.IsCancellationRequested
                    ? "Not tried — the sweep stopped after another model answered or the key hit its quota."
                    : "Not tried."
            });
        }

        var dto = new GeminiProbeDto
        {
            Winner = winner,
            Results = ordered,
            KeyConfigured = true,
            ProbeMode = probeMode
        };

        // Cache a completed sweep (winner found, or every model answered). A sweep
        // aborted by quota is left uncached so the retry button can work immediately.
        if (!cancellationToken.IsCancellationRequested && (winner is not null || ordered.All(r => r.Outcome != GeminiOutcomes.Skipped)))
        {
            _cache.Set(sweepKey, dto, SweepCacheTtl);
        }

        return dto;

        void Track(GeminiAttemptDto attempt)
        {
            if (attempt.Outcome == GeminiOutcomes.Ok)
            {
                winner ??= attempt.Model;
                stop.Cancel();
            }
            else if (attempt.Outcome is GeminiOutcomes.Quota or GeminiOutcomes.AuthError)
            {
                stop.Cancel();
            }
        }
    }

    /* ------------------------------------------------------------------ HTTP */

    private async Task<(GeminiAttemptDto Attempt, ReceiptScanDto? Scan)> CallAsync(
        string model,
        byte[] image,
        string mimeType,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var (attempt, text) = await SendAsync(
            model,
            BuildReceiptPayload(image, mimeType),
            apiKey,
            cancellationToken);

        if (attempt.Outcome != GeminiOutcomes.Ok || string.IsNullOrWhiteSpace(text))
        {
            return (attempt, null);
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<GeminiReceiptResponse>(text);
            if (parsed is null)
            {
                return (InvalidJson(model, attempt, "Gemini returned an unreadable receipt payload."), null);
            }

            return (attempt, Normalize(parsed, model));
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "Gemini returned invalid receipt JSON for model {Model}", model);
            return (InvalidJson(model, attempt, $"Gemini did not return JSON: {exception.Message}"), null);
        }
    }

    private async Task<(GeminiAttemptDto Attempt, string? Text)> SendAsync<T>(
        string model,
        T payload,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCancellation.CancelAfter(TimeSpan.FromSeconds(AttemptTimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"v1beta/models/{model}:generateContent");
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(payload);

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                attemptCancellation.Token);

            // Read the body with the caller's token: after a timeout we still want
            // whatever error payload Gemini managed to send.
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var failure = ClassifyFailure(model, response.StatusCode, body, stopwatch.ElapsedMilliseconds);
                _logger.LogWarning(
                    "Gemini model {Model} failed with {Outcome} ({Status}): {Message}",
                    model, failure.Outcome, (int)response.StatusCode, failure.Message);
                return (failure, null);
            }

            var text = ExtractText(body);
            if (string.IsNullOrWhiteSpace(text))
            {
                return (new GeminiAttemptDto
                {
                    Model = model,
                    Outcome = GeminiOutcomes.EmptyResponse,
                    Retryable = true,
                    HttpStatus = (int)response.StatusCode,
                    ElapsedMs = stopwatch.ElapsedMilliseconds,
                    Message = "Gemini returned no text.",
                    RawResponse = Truncate(body)
                }, null);
            }

            return (new GeminiAttemptDto
            {
                Model = model,
                Outcome = GeminiOutcomes.Ok,
                HttpStatus = (int)response.StatusCode,
                ElapsedMs = stopwatch.ElapsedMilliseconds
            }, text);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            _logger.LogWarning(
                exception,
                "Gemini model {Model} did not respond within {Seconds}s",
                model,
                AttemptTimeoutSeconds);
            return (new GeminiAttemptDto
            {
                Model = model,
                Outcome = GeminiOutcomes.Timeout,
                Retryable = true,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Message = $"No response within {AttemptTimeoutSeconds} seconds."
            }, null);
        }
        catch (JsonException exception)
        {
            stopwatch.Stop();
            return (new GeminiAttemptDto
            {
                Model = model,
                Outcome = GeminiOutcomes.InvalidJson,
                Retryable = true,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Message = exception.Message
            }, null);
        }
        catch (HttpRequestException exception)
        {
            stopwatch.Stop();
            _logger.LogWarning(exception, "Gemini request for model {Model} failed", model);
            return (new GeminiAttemptDto
            {
                Model = model,
                Outcome = GeminiOutcomes.UpstreamError,
                Retryable = true,
                ElapsedMs = stopwatch.ElapsedMilliseconds,
                Message = exception.Message
            }, null);
        }
    }

    private async Task<string> GetStringAsync(
        string uri,
        string apiKey,
        string label,
        CancellationToken cancellationToken)
    {
        using var attemptCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptCancellation.CancelAfter(TimeSpan.FromSeconds(AttemptTimeoutSeconds));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Add("x-goog-api-key", apiKey);
            using var response = await _httpClient.SendAsync(request, attemptCancellation.Token);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new GeminiScanException(
                    "Could not list the Gemini models for this API key.",
                    [ClassifyFailure(label, response.StatusCode, body, 0)]);
            }

            return body;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GeminiScanException(
                $"The Gemini model list did not respond within {AttemptTimeoutSeconds} seconds.",
                [
                    new GeminiAttemptDto
                    {
                        Model = label,
                        Outcome = GeminiOutcomes.Timeout,
                        Retryable = true,
                        Message = $"No response within {AttemptTimeoutSeconds} seconds."
                    }
                ],
                exception);
        }
    }

    /* --------------------------------------------------------- classification */

    private static GeminiAttemptDto ClassifyFailure(
        string model,
        HttpStatusCode status,
        string body,
        long elapsedMs)
    {
        var errorStatus = ReadErrorString(body, "status");
        var message = ReadErrorString(body, "message");
        var code = (int)status;

        var (outcome, retryable) = code switch
        {
            404 => (GeminiOutcomes.NotFound, false),
            429 => (GeminiOutcomes.Quota, true),
            503 => (GeminiOutcomes.Busy, true),
            401 or 403 => (GeminiOutcomes.AuthError, false),
            408 or 504 => (GeminiOutcomes.Timeout, true),
            400 or 422 => (GeminiOutcomes.InvalidRequest, false),
            >= 500 => (GeminiOutcomes.UpstreamError, true),
            _ => (GeminiOutcomes.InvalidRequest, false)
        };

        // error.status is more precise than the HTTP code and survives proxies
        // that rewrite the status line.
        switch (errorStatus?.ToUpperInvariant())
        {
            case "NOT_FOUND":
                (outcome, retryable) = (GeminiOutcomes.NotFound, false);
                break;
            case "RESOURCE_EXHAUSTED":
                (outcome, retryable) = (GeminiOutcomes.Quota, true);
                break;
            case "UNAVAILABLE":
                (outcome, retryable) = (GeminiOutcomes.Busy, true);
                break;
            case "DEADLINE_EXCEEDED":
                (outcome, retryable) = (GeminiOutcomes.Timeout, true);
                break;
            case "UNAUTHENTICATED":
            case "PERMISSION_DENIED":
                (outcome, retryable) = (GeminiOutcomes.AuthError, false);
                break;
            case "INVALID_ARGUMENT":
            case "FAILED_PRECONDITION":
                (outcome, retryable) = (GeminiOutcomes.InvalidRequest, false);
                break;
            case "INTERNAL":
                (outcome, retryable) = (GeminiOutcomes.UpstreamError, true);
                break;
        }

        // A 400 that complains about the image part means "text-only model",
        // which is a different answer from "your request was malformed".
        if (outcome == GeminiOutcomes.InvalidRequest && message is not null && ImageRejection.IsMatch(message))
        {
            outcome = GeminiOutcomes.NoImageSupport;
        }

        var suggestedModel = outcome == GeminiOutcomes.NotFound
            ? ExtractSuggestedModel(message)
            : null;

        return new GeminiAttemptDto
        {
            Model = model,
            Outcome = outcome,
            Retryable = retryable,
            HttpStatus = code,
            ErrorStatus = errorStatus,
            ElapsedMs = elapsedMs,
            Message = message,
            SuggestedModel = suggestedModel,
            RawResponse = Truncate(body)
        };
    }

    private static string? ExtractSuggestedModel(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var match = SuggestedModelPattern.Match(message);
        if (!match.Success)
        {
            match = AnyModelPattern.Match(message);
        }

        return match.Success && TryNormalizeModelName(match.Groups[1].Value, out var normalized)
            ? normalized
            : null;
    }

    private static string? ReadErrorString(string body, string property)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.TryGetProperty("error", out var error) &&
                   error.ValueKind == JsonValueKind.Object &&
                   error.TryGetProperty(property, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractText(string body)
    {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("candidates", out var candidates) ||
            candidates.ValueKind != JsonValueKind.Array ||
            candidates.GetArrayLength() == 0)
        {
            return null;
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content) ||
            !content.TryGetProperty("parts", out var parts) ||
            parts.ValueKind != JsonValueKind.Array ||
            parts.GetArrayLength() == 0)
        {
            return null;
        }

        return parts[0].TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String
            ? text.GetString()
            : null;
    }

    private static string? Truncate(string? body)
    {
        if (body is null)
        {
            return null;
        }

        return body.Length <= MaxRawResponseChars
            ? body
            : string.Concat(body.AsSpan(0, MaxRawResponseChars), $"…truncated ({body.Length} chars)");
    }

    /* -------------------------------------------------------------- plumbing */

    private List<string> BuildChain(string? requested, bool strict)
    {
        var chain = new List<string>();
        if (requested is not null)
        {
            chain.Add(requested);
        }

        if (strict && chain.Count == 0)
        {
            chain.Add(DefaultModel);
        }
        else if (!strict && !chain.Contains(DefaultModel, StringComparer.OrdinalIgnoreCase))
        {
            chain.Add(DefaultModel);
        }

        return chain.Count == 0 ? [DefaultModel] : chain;
    }

    private List<string> BuildCandidateList(IReadOnlyList<string>? models, string defaultModel)
    {
        var candidates = new List<string>();

        void Add(string? name)
        {
            if (TryNormalizeModelName(name, out var normalized) &&
                !candidates.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                candidates.Add(normalized);
            }
        }

        if (models is { Count: > 0 })
        {
            foreach (var model in models)
            {
                Add(model);
            }
        }
        else
        {
            Add(defaultModel);
        }

        if (candidates.Count == 0)
        {
            Add(defaultModel);
        }

        return candidates.Take(MaxProbeModels).ToList();
    }

    /// <summary>
    /// Skips a model without spending a request when its metadata or its name
    /// family already rules out a receipt scan.
    /// </summary>
    private static GeminiAttemptDto? ClassifyWithoutCall(
        string model,
        IReadOnlyDictionary<string, GeminiModelDto>? metadata)
    {
        if (metadata is not null &&
            metadata.TryGetValue(model, out var entry) &&
            !entry.SupportsGenerateContent)
        {
            return new GeminiAttemptDto
            {
                Model = model,
                Outcome = GeminiOutcomes.NoGenerateContent,
                Message = "Gemini lists this model without generateContent, so it cannot produce a receipt."
            };
        }

        if (NotAReceiptReader.IsMatch(model))
        {
            return new GeminiAttemptDto
            {
                Model = model,
                Outcome = GeminiOutcomes.Unsupported,
                Message = "This model family does not read images (text-to-speech, embeddings, image generation, ...)."
            };
        }

        return null;
    }

    private async Task<IReadOnlyDictionary<string, GeminiModelDto>?> TryGetModelIndexAsync(
        string apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var models = await ListModelsAsync(cancellationToken);
            return models.Models.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is GeminiScanException or HttpRequestException or JsonException)
        {
            // The probe does not depend on the catalogue; carry on without it.
            _logger.LogWarning(exception, "Could not load the Gemini model catalogue before probing");
            return null;
        }
    }

    private static TimeSpan? CacheLifetime(string outcome) => outcome switch
    {
        GeminiOutcomes.Ok => TimeSpan.FromHours(24),
        GeminiOutcomes.NotFound => TimeSpan.FromHours(24),
        GeminiOutcomes.NoGenerateContent => TimeSpan.FromHours(24),
        GeminiOutcomes.Unsupported => TimeSpan.FromHours(24),
        GeminiOutcomes.NoImageSupport => TimeSpan.FromHours(24),
        GeminiOutcomes.InvalidRequest => TimeSpan.FromHours(24),
        GeminiOutcomes.AuthError => TimeSpan.FromHours(24),
        GeminiOutcomes.Quota => TimeSpan.FromSeconds(60),
        GeminiOutcomes.Busy => TimeSpan.FromMinutes(5),
        GeminiOutcomes.UpstreamError => TimeSpan.FromMinutes(5),
        GeminiOutcomes.Timeout => TimeSpan.FromMinutes(10),
        GeminiOutcomes.EmptyResponse => TimeSpan.FromMinutes(30),
        GeminiOutcomes.InvalidJson => TimeSpan.FromMinutes(30),
        _ => null
    };

    private string? GetApiKey()
    {
        // Configuration includes the environment in the real host, so GEMINI_API_KEY
        // works whether it comes from appsettings, user-secrets, or the container.
        var key = _configuration["GEMINI_API_KEY"];
        return string.IsNullOrWhiteSpace(key) ? null : key.Trim();
    }

    private static string? NormalizeModelName(string? value) =>
        TryNormalizeModelName(value, out var normalized) ? normalized : null;

    private static byte[]? LoadProbeImage(ILogger logger)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Samples", "receipt-probe.jpg");
            if (!File.Exists(path))
            {
                logger.LogWarning(
                    "Probe image not found at {Path}. Availability sweeps will use a text prompt instead.",
                    path);
                return null;
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the probe image. Availability sweeps will use a text prompt instead.");
            return null;
        }
    }

    /* ------------------------------------------------------------ request/response */

    private static object BuildReceiptPayload(byte[] image, string mimeType) => new
    {
        contents = new[]
        {
            new
            {
                parts = new object[]
                {
                    new { text = Prompt },
                    new
                    {
                        inline_data = new
                        {
                            mime_type = mimeType,
                            data = Convert.ToBase64String(image)
                        }
                    }
                }
            }
        },
        generationConfig = new
        {
            temperature = 0,
            responseMimeType = "application/json",
            responseSchema = ResponseSchema
        }
    };

    private static readonly object TextProbePayload = new
    {
        contents = new[]
        {
            new { parts = new[] { new { text = "Reply with exactly: OK" } } }
        }
    };

    private static ReceiptScanDto Normalize(GeminiReceiptResponse result, string model)
    {
        DateTime? date = null;
        if (DateTime.TryParse(
                result.Date,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsedDate))
        {
            date = parsedDate;
        }

        var items = result.Items
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Select(item => new ReceiptScanItemDto
            {
                Name = item.Name!.Trim(),
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice,
                Amount = item.Amount
            })
            .ToList();
        var itemTotal = items.Sum(item => item.Amount ?? 0);
        var itemsMatchTotal = !result.Total.HasValue ||
                              Math.Abs(itemTotal - result.Total.Value) < 0.01m;

        return new ReceiptScanDto
        {
            Merchant = result.Merchant?.Trim(),
            Date = date,
            Currency = result.Currency?.Trim(),
            Items = items,
            Subtotal = result.Subtotal,
            Discount = result.Discount,
            Total = result.Total,
            RawTotalText = result.RawTotalText?.Trim(),
            ItemsMatchTotal = itemsMatchTotal,
            NeedsReview = !itemsMatchTotal || !result.Total.HasValue || items.Count == 0,
            ModelUsed = model
        };
    }

    private static GeminiAttemptDto InvalidJson(string model, GeminiAttemptDto attempt, string message) =>
        new()
        {
            Model = model,
            Outcome = GeminiOutcomes.InvalidJson,
            Retryable = true,
            HttpStatus = attempt.HttpStatus,
            ElapsedMs = attempt.ElapsedMs,
            Message = message
        };

    private static string? ReadModelsPage(string body, List<GeminiModelDto> models)
    {
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("models", out var list) && list.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in list.EnumerateArray())
            {
                if (!TryNormalizeModelName(ReadString(entry, "name"), out var name))
                {
                    continue;
                }

                var methods = entry.TryGetProperty("supportedGenerationMethods", out var methodList) &&
                              methodList.ValueKind == JsonValueKind.Array
                    ? methodList.EnumerateArray()
                        .Where(m => m.ValueKind == JsonValueKind.String)
                        .Select(m => m.GetString()!)
                        .ToArray()
                    : [];

                models.Add(new GeminiModelDto
                {
                    Name = name,
                    Version = ReadString(entry, "version"),
                    DisplayName = ReadString(entry, "displayName"),
                    Description = ReadString(entry, "description"),
                    InputTokenLimit = ReadLong(entry, "inputTokenLimit"),
                    OutputTokenLimit = ReadLong(entry, "outputTokenLimit"),
                    SupportedGenerationMethods = methods,
                    SupportsGenerateContent = methods.Contains("generateContent", StringComparer.OrdinalIgnoreCase)
                });
            }
        }

        return ReadString(root, "nextPageToken") is { Length: > 0 } token ? token : null;
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var number)
            ? number
            : null;

    /* -------------------------------------------------------------- schema */

    private static readonly object ResponseSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            merchant = new { type = "STRING" },
            date = new { type = "STRING", description = "ISO date YYYY-MM-DD, or empty string if unavailable" },
            currency = new { type = "STRING" },
            items = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        name = new { type = "STRING" },
                        quantity = new { type = "NUMBER" },
                        unitPrice = new { type = "NUMBER" },
                        amount = new { type = "NUMBER" }
                    },
                    required = new[] { "name", "amount" }
                }
            },
            subtotal = new { type = "NUMBER" },
            discount = new { type = "NUMBER" },
            total = new { type = "NUMBER" },
            rawTotalText = new { type = "STRING" }
        },
        required = new[] { "merchant", "date", "currency", "items", "total", "rawTotalText" }
    };

    private const string Prompt = """
        Extract this receipt into the JSON schema. Do not invent values. Use null only where the schema permits it, and use an empty string for unavailable text fields. Include every visible purchased line item, its quantity, unit price, and final line amount. Preserve the receipt's total text in rawTotalText. The total must be the final amount charged, not a subtotal.
        """;
}
