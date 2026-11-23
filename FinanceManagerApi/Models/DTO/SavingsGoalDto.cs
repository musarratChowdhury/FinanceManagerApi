namespace FinanceManagerApi.Models.DTO;

/// <summary>One top-up on a savings goal.</summary>
public class GoalContributionDto
{
    public long Id { get; set; }
    public double Amount { get; set; }
    public DateTime ContributionDate { get; set; }
}

/// <summary>A savings goal with every derived figure the card displays.</summary>
public class SavingsGoalDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double TargetAmount { get; set; }

    /// <summary>Sum of the contributions — never stored on the goal row.</summary>
    public double SavedAmount { get; set; }

    public DateTime? TargetDate { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Saved so far divided by the months since the first contribution.</summary>
    public double AvgSavedPerMonth { get; set; }

    /// <summary>Monthly amount still required to hit the target date. Null when no date is set.</summary>
    public double? NeededPerMonth { get; set; }

    /// <summary>Whole months until the target date, clamped at 0.</summary>
    public int? MonthsRemaining { get; set; }

    /// <summary>reached | noSavings | noTarget | onTrack | late</summary>
    public string PaceStatus { get; set; } = "noSavings";

    /// <summary>Date the current average would reach the target, when there is an average.</summary>
    public DateTime? ProjectedReachDate { get; set; }

    public IEnumerable<GoalContributionDto> Contributions { get; set; } = Enumerable.Empty<GoalContributionDto>();
}

/// <summary>Request body for a new goal; the first amount set aside becomes a contribution.</summary>
public class CreateSavingsGoalDto
{
    public string Name { get; set; } = string.Empty;
    public double TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
    public double? InitialSaved { get; set; }
}

/// <summary>Request body for editing a goal. Only these fields can change.</summary>
public class UpdateSavingsGoalDto
{
    public string Name { get; set; } = string.Empty;
    public double TargetAmount { get; set; }
    public DateTime? TargetDate { get; set; }
}

/// <summary>Request body for "Add to saved".</summary>
public class AddGoalContributionDto
{
    public double Amount { get; set; }

    /// <summary>Defaults to now when omitted.</summary>
    public DateTime? ContributionDate { get; set; }
}
