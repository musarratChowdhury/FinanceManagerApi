namespace FinanceManagerApi.Services.Storage;

/// <summary>
/// The object store refused or failed a request. Translated to a 502 by the
/// receipt-photo service; the original exception stays in the logs.
/// </summary>
public sealed class ReceiptStorageException : Exception
{
    public ReceiptStorageException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
