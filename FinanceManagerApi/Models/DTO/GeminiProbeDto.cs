namespace FinanceManagerApi.Models.DTO;

public sealed class GeminiProbeRequest
{
    /// <summary>
    /// Models to try, in priority order. Null or empty probes the whole list from
    /// the models endpoint, with the server default first.
    /// </summary>
    public List<string>? Models { get; init; }

    /// <summary>Bypass the cached availability results and hit Gemini again.</summary>
    public bool Fresh { get; init; }
}

public sealed class GeminiProbeDto
{
    /// <summary>First model that answered successfully; null when none did.</summary>
    public string? Winner { get; init; }

    /// <summary>One entry per candidate, in the order they were tried.</summary>
    public IReadOnlyList<GeminiAttemptDto> Results { get; init; } = [];

    public bool KeyConfigured { get; init; }

    /// <summary>receipt = probed with the sample receipt image, text = image fixture unavailable.</summary>
    public string ProbeMode { get; init; } = ProbeModes.Receipt;

    /// <summary>True when the results came from the server cache rather than a fresh sweep.</summary>
    public bool Cached { get; init; }

    public DateTime CheckedAt { get; init; } = DateTime.UtcNow;
}

public static class ProbeModes
{
    public const string Receipt = "receipt";
    public const string Text = "text";
}
