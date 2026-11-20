namespace FinanceManagerApi.Models.DTO;

/// <summary>
/// A single application setting as exposed by the settings API.
/// </summary>
public sealed class SettingDto
{
    public string Key { get; init; } = string.Empty;

    /// <summary>Raw value as stored (a JSON string).</summary>
    public string Value { get; init; } = string.Empty;

    public string? Description { get; init; }
}

/// <summary>
/// Upsert payload for one setting. The value is a JSON string, so a client can
/// send <c>"gemini-3.8-flash"</c> (with the quotes) or any JSON literal.
/// </summary>
public sealed class UpdateSettingDto
{
    public string Value { get; init; } = string.Empty;

    public string? Description { get; init; }
}

/// <summary>
/// Bulk upsert payload: many key/value pairs in one call, which is what the
/// settings page uses to save the whole form at once.
/// </summary>
public sealed class UpdateSettingsDto
{
    public Dictionary<string, string> Values { get; init; } = [];
}
