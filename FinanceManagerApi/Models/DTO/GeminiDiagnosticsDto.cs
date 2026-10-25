namespace FinanceManagerApi.Models.DTO;

/// <summary>
/// One attempt against one Gemini model. Every failure the API can describe to
/// a user goes through this shape, including the raw upstream body, because the
/// interesting part of a Gemini error is almost always the JSON it sent back
/// (retired model + suggested replacement, quota + retryDelay, ...).
/// </summary>
public sealed class GeminiAttemptDto
{
    public string Model { get; init; } = string.Empty;

    /// <summary>ok | not_found | busy | quota | auth_error | invalid_request | timeout | upstream_error | empty_response | invalid_json | skipped</summary>
    public string Outcome { get; init; } = GeminiOutcomes.Unknown;

    /// <summary>True when trying the same model again later could succeed.</summary>
    public bool Retryable { get; init; }

    public int? HttpStatus { get; init; }

    /// <summary>Gemini's `error.status`, e.g. NOT_FOUND or UNAVAILABLE.</summary>
    public string? ErrorStatus { get; init; }

    public long ElapsedMs { get; init; }

    /// <summary>Gemini's `error.message`, which is already written for humans.</summary>
    public string? Message { get; init; }

    /// <summary>Replacement model extracted from a deprecation message, if any.</summary>
    public string? SuggestedModel { get; init; }

    /// <summary>Verbatim upstream response body, truncated. Null when there was no response.</summary>
    public string? RawResponse { get; init; }
}

/// <summary>Body returned with a 502 when every model in the chain failed.</summary>
public sealed class GeminiFailureDto
{
    public string Message { get; init; } = string.Empty;
    public IReadOnlyList<GeminiAttemptDto> Attempts { get; init; } = [];
}

public static class GeminiOutcomes
{
    public const string Unknown = "unknown";
    public const string Ok = "ok";

    /// <summary>Retired, or not available to this API key.</summary>
    public const string NotFound = "not_found";

    /// <summary>503 UNAVAILABLE — "high demand, try again later".</summary>
    public const string Busy = "busy";

    /// <summary>429 RESOURCE_EXHAUSTED.</summary>
    public const string Quota = "quota";

    public const string AuthError = "auth_error";
    public const string InvalidRequest = "invalid_request";
    public const string Timeout = "timeout";
    public const string UpstreamError = "upstream_error";
    public const string EmptyResponse = "empty_response";
    public const string InvalidJson = "invalid_json";

    /// <summary>Not attempted: an earlier model answered, or the sweep stopped.</summary>
    public const string Skipped = "skipped";

    /// <summary>The model's metadata does not list generateContent at all.</summary>
    public const string NoGenerateContent = "no_generate_content";

    /// <summary>Callable, but not a receipt reader (TTS, embeddings, image generation, ...).</summary>
    public const string Unsupported = "unsupported";

    /// <summary>Gemini rejected the image part — text-only model.</summary>
    public const string NoImageSupport = "no_image_support";
}
