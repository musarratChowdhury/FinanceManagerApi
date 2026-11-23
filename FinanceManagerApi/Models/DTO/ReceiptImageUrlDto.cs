namespace FinanceManagerApi.Models.DTO;

/// <summary>
/// A presigned download URL for a receipt photo, plus when it stops working so
/// the client can refetch instead of showing a broken image.
/// </summary>
public class ReceiptImageUrlDto
{
    public string Url { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }
}
