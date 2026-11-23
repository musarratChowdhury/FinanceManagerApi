namespace FinanceManagerApi.Services.Storage;

/// <summary>
/// The API's view of the private receipt-photo store. Kept narrow so the
/// receipt-photo service can be tested without an S3 endpoint.
/// </summary>
public interface IReceiptStorage
{
    /// <summary>
    /// False when credentials are missing. Only the receipt-photo endpoints care;
    /// everything else on the API keeps working.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Stores <paramref name="data"/> under <paramref name="key"/>. The length is
    /// passed explicitly so a store can set Content-Length without probing.
    /// </summary>
    Task PutAsync(
        string key,
        Stream data,
        long length,
        string contentType,
        CancellationToken cancellationToken = default);

    /// <summary>Removes <paramref name="key"/>. Deleting a missing object succeeds.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Short-lived download URL. The client never sees the object key itself, so
    /// this is the only way a photo reaches a browser.
    /// </summary>
    string GetSignedUrl(string key, TimeSpan ttl);
}
