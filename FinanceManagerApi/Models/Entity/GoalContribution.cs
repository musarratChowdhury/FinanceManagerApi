namespace FinanceManagerApi.Models.Entity;

/// <summary>
/// One amount set aside towards a <see cref="SavingsGoal"/>.
///
/// A mistake is fixed by deleting the row, never by editing a running total, which
/// is what keeps "average saved per month" honest.
/// </summary>
public class GoalContribution
{
    public long Id { get; set; }

    public long SavingsGoalId { get; set; }
    public SavingsGoal? SavingsGoal { get; set; }

    public double Amount { get; set; }

    /// <summary>When the money was set aside (UTC midnight is fine).</summary>
    public DateTime ContributionDate { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Owner, mirroring the goal's owner for a cheap scoping check.</summary>
    public Guid CreatedBy { get; set; }
}
