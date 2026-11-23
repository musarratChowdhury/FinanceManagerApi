namespace FinanceManagerApi.Models.Entity;

/// <summary>
/// The softcopy of a receipt: one image per receipt, stored in the private
/// receipt-photo bucket. The row is the source of truth — <see cref="ObjectKey"/>
/// points at the bytes, and deleting the receipt deletes both.
/// </summary>
public class ReceiptImage
{
    public long Id { get; set; }

    /// <summary>Owning receipt. Unique: a receipt holds at most one photo.</summary>
    public long ReceiptId { get; set; }

    public Receipt? Receipt { get; set; }

    /// <summary>
    /// Server-built object key (<c>receipts/{userId}/{receiptId}/{guid}.{ext}</c>).
    /// Never derived from a client filename.
    /// </summary>
    public string ObjectKey { get; set; } = string.Empty;

    /// <summary>Sniffed content type: image/jpeg, image/png or image/webp.</summary>
    public string ContentType { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    /// <summary>When the photo was stored — server owned.</summary>
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who uploaded it. Denormalised from the receipt so the per-user quota is a
    /// single indexed SUM instead of a join.
    /// </summary>
    public Guid CreatedBy { get; set; }
}
