namespace FinanceManagerApi.Models.Errors;

/// <summary>
/// A receipt-photo request that should answer with a specific status instead of
/// the generic 500 from <c>ExceptionMiddleware</c>: 400 bad file, 404 foreign or
/// missing receipt, 413 too large, 422 quota, 502 store failure, 503 storage not
/// configured.
/// </summary>
public sealed class ReceiptImageException : Exception
{
    public ReceiptImageException(int statusCode, string message, object? payload = null)
        : base(message)
    {
        StatusCode = statusCode;
        Payload = payload;
    }

    public int StatusCode { get; }

    /// <summary>Optional response body; defaults to <c>{ message }</c>.</summary>
    public object? Payload { get; }
}
