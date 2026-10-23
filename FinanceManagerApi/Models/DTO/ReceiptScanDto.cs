using System.Text.Json.Serialization;

namespace FinanceManagerApi.Models.DTO;

public sealed class ReceiptScanDto
{
    public string? Merchant { get; init; }
    public DateTime? Date { get; init; }
    public string? Currency { get; init; }
    public List<ReceiptScanItemDto> Items { get; init; } = [];
    public decimal? Subtotal { get; init; }
    public decimal? Discount { get; init; }
    public decimal? Total { get; init; }
    public string? RawTotalText { get; init; }
    public bool ItemsMatchTotal { get; init; }
    public bool NeedsReview { get; init; }
}

public sealed class ReceiptScanItemDto
{
    public string Name { get; init; } = string.Empty;
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public decimal? Amount { get; init; }
}

internal sealed class GeminiReceiptResponse
{
    [JsonPropertyName("merchant")]
    public string? Merchant { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("currency")]
    public string? Currency { get; set; }

    [JsonPropertyName("items")]
    public List<GeminiReceiptItem> Items { get; set; } = [];

    [JsonPropertyName("subtotal")]
    public decimal? Subtotal { get; set; }

    [JsonPropertyName("discount")]
    public decimal? Discount { get; set; }

    [JsonPropertyName("total")]
    public decimal? Total { get; set; }

    [JsonPropertyName("rawTotalText")]
    public string? RawTotalText { get; set; }
}

internal sealed class GeminiReceiptItem
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal? UnitPrice { get; set; }

    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }
}
