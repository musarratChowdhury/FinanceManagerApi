namespace FinanceManagerApi.Models.DTO;

/// <summary>
/// A stored receipt photo, as the client sees it. The object key is deliberately
/// absent: the only way to reach the bytes is a short-lived signed URL.
/// </summary>
public class ReceiptImageDto
{
    public long Id { get; set; }

    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public DateTime UploadedAt { get; set; }
}
