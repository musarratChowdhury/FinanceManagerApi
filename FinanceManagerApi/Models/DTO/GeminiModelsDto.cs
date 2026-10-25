namespace FinanceManagerApi.Models.DTO;

/// <summary>
/// A model the configured API key can see. The list is deliberately unfiltered:
/// the settings screen shows every model and marks the ones that cannot run a
/// receipt scan, rather than silently hiding them.
/// </summary>
public sealed class GeminiModelDto
{
    /// <summary>Bare model id, without the `models/` prefix.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Gemini's version string, e.g. gemini-2.5-pro-preview-tts-2025-05-19.</summary>
    public string? Version { get; init; }

    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public long? InputTokenLimit { get; init; }
    public long? OutputTokenLimit { get; init; }

    /// <summary>Raw supportedGenerationMethods, as reported by Gemini.</summary>
    public IReadOnlyList<string> SupportedGenerationMethods { get; init; } = [];

    /// <summary>False for models that cannot produce content at all (embeddings, aqa, ...).</summary>
    public bool SupportsGenerateContent { get; init; }
}

public sealed class GeminiModelsDto
{
    public IReadOnlyList<GeminiModelDto> Models { get; init; } = [];

    /// <summary>Server fallback model, used when the client has not chosen one.</summary>
    public string DefaultModel { get; init; } = string.Empty;

    /// <summary>False when the server has no GEMINI_API_KEY; the UI shows setup help instead of a dropdown.</summary>
    public bool KeyConfigured { get; init; }

    public int AttemptTimeoutSeconds { get; init; }
    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}
