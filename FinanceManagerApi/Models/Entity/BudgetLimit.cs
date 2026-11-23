namespace FinanceManagerApi.Models.Entity;

/// <summary>
/// A user's monthly ceiling for one expense category.
///
/// There is exactly one row per (user, category): the ceiling carries forward into
/// every new month, while the spend it is measured against is always computed for
/// the month being viewed — so spending "resets" on the 1st but the limit does not.
/// </summary>
public class BudgetLimit
{
    public long Id { get; set; }

    public long ExpenseCategoryId { get; set; }
    public ExpenseCategory? ExpenseCategory { get; set; }

    /// <summary>Ceiling for one month, in the account currency.</summary>
    public double MonthlyLimit { get; set; }

    /// <summary>
    /// First day (UTC) of the month the current value was set, so the screen can say
    /// "the limit applies from November 2026 onward". Informational only — there is
    /// no per-month history.
    /// </summary>
    public DateTime EffectiveFrom { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Owner. Every read is filtered by it.</summary>
    public Guid CreatedBy { get; set; }
}
