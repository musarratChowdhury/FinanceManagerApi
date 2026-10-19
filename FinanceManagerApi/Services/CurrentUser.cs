using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace FinanceManagerApi.Services;

/// <summary>
/// Who is making the current request. Expenses, Incomes and their month queries are private to the
/// user who created them, so every read has to be scoped to <see cref="UserId"/>.
/// </summary>
public interface ICurrentUser
{
    /// <summary>
    /// Id of the authenticated user, taken from the JWT's NameIdentifier claim.
    /// Throws when the request has no usable user id, so a query can never silently fall back to
    /// an unfiltered (or Guid.Empty-owned) result set.
    /// </summary>
    Guid UserId { get; }

    /// <summary>True when the current request carries a parseable user id claim.</summary>
    bool IsAuthenticated { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => TryGetUserId(out _);

    public Guid UserId => TryGetUserId(out var userId)
        ? userId
        : throw new UnauthorizedAccessException(
            "The current request has no NameIdentifier claim, so no user-owned data can be queried.");

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;

        var claim = _httpContextAccessor.HttpContext?.User
            .FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return !string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out userId);
    }
}
