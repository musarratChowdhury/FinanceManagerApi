namespace FinanceManagerApi.Models.Entity;

/// <summary>
/// Simple key-value application settings store.
/// Values are persisted as JSON strings so a single table can hold
/// strings, numbers and booleans without a schema change per setting.
/// </summary>
public class Settings
{
    public long Id { get; set; }

    /// <summary>Unique setting key, e.g. "ReceiptScanModel".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Setting value serialized as JSON.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>Optional human-readable description of the setting.</summary>
    public string? Description { get; set; }
}
