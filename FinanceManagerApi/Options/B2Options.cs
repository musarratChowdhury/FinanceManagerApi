namespace FinanceManagerApi.Options;

/// <summary>
/// Backblaze B2 (S3-compatible API) settings for the receipt photo bucket.
///
/// The credentials are deliberately empty by default: the API must boot and
/// serve receipts without storage configured, and only the receipt-photo
/// endpoints report 503 until they are set. Never commit real values here —
/// use user-secrets locally and <c>B2__*</c> environment variables in production.
/// </summary>
public class B2Options
{
    /// <summary>Bucket endpoint with the scheme, e.g. https://s3.us-west-004.backblazeb2.com.</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Region from the middle of the endpoint, e.g. us-west-004.</summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>Application key id (not the master key).</summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>Application key secret. Shown once at creation time.</summary>
    public string AppKey { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    /// <summary>Largest receipt photo accepted, in bytes.</summary>
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;

    /// <summary>Total bytes one user may store across all of their receipts.</summary>
    public long QuotaBytesPerUser { get; set; } = 200L * 1024 * 1024;

    /// <summary>Lifetime of a presigned download URL, in seconds.</summary>
    public int SignedUrlTtlSeconds { get; set; } = 300;

    /// <summary>
    /// True when every credential is present. A partial configuration is treated
    /// as unconfigured rather than failing a request half way through.
    /// </summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ServiceUrl) &&
        !string.IsNullOrWhiteSpace(Region) &&
        !string.IsNullOrWhiteSpace(KeyId) &&
        !string.IsNullOrWhiteSpace(AppKey) &&
        !string.IsNullOrWhiteSpace(Bucket);
}
