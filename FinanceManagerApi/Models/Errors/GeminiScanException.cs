using FinanceManagerApi.Models.DTO;

namespace FinanceManagerApi.Models.Errors;

/// <summary>
/// Raised when every model in the chain failed. Carries the per-attempt detail so
/// the controller can answer with a 502 body instead of the generic message
/// <c>ExceptionMiddleware</c> would otherwise produce in production.
/// </summary>
public sealed class GeminiScanException : Exception
{
    public GeminiScanException(
        string message,
        IReadOnlyList<GeminiAttemptDto> attempts,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Attempts = attempts;
    }

    public IReadOnlyList<GeminiAttemptDto> Attempts { get; }
}
