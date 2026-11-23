namespace FinanceManagerApi.Models.Entity;

/// <summary>
/// Something the user is saving towards.
///
/// "Saved so far" is never stored on this row: it is the sum of the goal's
/// <see cref="GoalContribution"/> rows, so every top-up stays auditable and the
/// average-per-month / pace figures are derived from real history rather than
/// from a single overwritten number.
/// </summary>
public class SavingsGoal
{
    public long Id { get; set; }

    /// <summary>What the user is saving for, e.g. "new pc".</summary>
    public string Name { get; set; } = string.Empty;

    public double TargetAmount { get; set; }

    /// <summary>Optional date the user wants to reach the target by.</summary>
    public DateTime? TargetDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Owner. Every read is filtered by it.</summary>
    public Guid CreatedBy { get; set; }

    public ICollection<GoalContribution> Contributions { get; set; } = new List<GoalContribution>();
}
