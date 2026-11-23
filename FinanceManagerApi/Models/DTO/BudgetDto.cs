namespace FinanceManagerApi.Models.DTO;

/// <summary>One category ceiling, as returned by a save.</summary>
public class BudgetLimitDto
{
    public long Id { get; set; }
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;
    public double MonthlyLimit { get; set; }
    public DateTime EffectiveFrom { get; set; }
}

/// <summary>Request body for "Save budget" (upsert).</summary>
public class SaveBudgetLimitDto
{
    public long CategoryId { get; set; }
    public double MonthlyLimit { get; set; }
}

/// <summary>One card in the "By category" grid.</summary>
public class BudgetCategoryDto
{
    public long CategoryId { get; set; }
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>0 when the category has no ceiling.</summary>
    public double Limit { get; set; }

    public double Spent { get; set; }
    public double UsedPercent { get; set; }

    /// <summary>over | near | onTrack | noBudget</summary>
    public string Status { get; set; } = "noBudget";

    /// <summary>Null for a category without a ceiling.</summary>
    public DateTime? EffectiveFrom { get; set; }
}

/// <summary>
/// Everything the Budgets screen needs for one month. The maths lives here rather
/// than in the client so the projection and safe-to-spend figures are testable and
/// identical for every consumer.
/// </summary>
public class BudgetMonthDto
{
    public int Month { get; set; }
    public int Year { get; set; }

    public double TotalLimit { get; set; }
    public double TotalSpent { get; set; }

    /// <summary>Linear run-rate projection for the whole month.</summary>
    public double Projected { get; set; }

    public double UsedPercent { get; set; }
    public double SafeToSpendPerDay { get; set; }

    public int DaysInMonth { get; set; }
    public int DaysElapsed { get; set; }
    public int DaysRemaining { get; set; }

    public IEnumerable<BudgetCategoryDto> Categories { get; set; } = Enumerable.Empty<BudgetCategoryDto>();
}
