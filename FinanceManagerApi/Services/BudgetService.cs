using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface IBudgetService
{
    /// <summary>Month summary: limits, spend, projection and safe-to-spend per day.</summary>
    Task<BudgetMonthDto> GetMonthAsync(int month, int year);

    /// <summary>Insert or update the caller's ceiling for one category. Null when the category does not exist.</summary>
    Task<BudgetLimitDto?> UpsertLimitAsync(SaveBudgetLimitDto dto);

    /// <summary>Remove the caller's ceiling for a category. False when there was none.</summary>
    Task<bool> DeleteLimitAsync(long categoryId);
}

/// <summary>
/// Category ceilings for the Budgets screen.
///
/// Two rules drive everything here:
/// 1. A limit is a single current value per (user, category) that carries forward every
///    month — it is not copied per month, so a new month starts with the same ceiling.
/// 2. Spend is always computed for the month being viewed, from the user's own expenses,
///    so spending resets on the 1st without touching the limit.
///
/// The projection is a plain linear run-rate (spend so far scaled to the whole month),
/// and "safe to spend per day" is what is left of the ceiling spread over the remaining
/// days — both are deliberately simple and stated on screen so the user can judge them.
/// </summary>
public class BudgetService : IBudgetService
{
    private readonly ILogger<BudgetService> _logger;
    private readonly IGenericRepository<BudgetLimit> _budgetRepository;
    private readonly IGenericRepository<ExpenseCategory> _categoryRepository;
    private readonly IExpenseService _expenseService;
    private readonly ICurrentUser _currentUser;

    public BudgetService(
        ILogger<BudgetService> logger,
        IGenericRepository<BudgetLimit> budgetRepository,
        IGenericRepository<ExpenseCategory> categoryRepository,
        IExpenseService expenseService,
        ICurrentUser currentUser)
    {
        _logger = logger;
        _budgetRepository = budgetRepository;
        _categoryRepository = categoryRepository;
        _expenseService = expenseService;
        _currentUser = currentUser;
    }

    public async Task<BudgetMonthDto> GetMonthAsync(int month, int year)
    {
        if (month < 1 || month > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(month), "Month number must be between 1 and 12.");
        }

        var currentYear = DateTime.UtcNow.Year;
        if (year < 1 || year > currentYear + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year), $"Year must be between 1 and {currentYear + 1}.");
        }

        try
        {
            var firstDayOfMonth = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var daysInMonth = DateTime.DaysInMonth(year, month);
            var daysElapsed = ResolveDaysElapsed(firstDayOfMonth, daysInMonth);
            var daysRemaining = daysInMonth - daysElapsed;

            var limits = (await _budgetRepository.Filter(
                x => x.CreatedBy == _currentUser.UserId,
                "ExpenseCategory")).ToList();

            // Spend is already grouped per category and scoped to the caller by the
            // expense service, so the same predicate is not duplicated here.
            var groups = (await _expenseService.GetExpensesOfAMonthAsync(month, year)).ToList();

            var limitByCategory = new Dictionary<long, BudgetLimit>();
            foreach (var limit in limits)
            {
                limitByCategory[limit.ExpenseCategoryId] = limit;
            }

            var spentByCategory = new Dictionary<long, double>();
            foreach (var group in groups)
            {
                spentByCategory[group.CategoryId] = group.TotalAmount;
            }

            var categoryIds = new SortedSet<long>(limitByCategory.Keys);
            foreach (var group in groups)
            {
                categoryIds.Add(group.CategoryId);
            }

            var categories = new List<BudgetCategoryDto>(categoryIds.Count);
            foreach (var categoryId in categoryIds)
            {
                limitByCategory.TryGetValue(categoryId, out var limit);
                spentByCategory.TryGetValue(categoryId, out var spent);

                var limitAmount = limit?.MonthlyLimit ?? 0;
                var name = limit?.ExpenseCategory?.Name
                    ?? groups.FirstOrDefault(g => g.CategoryId == categoryId)?.CategoryName
                    ?? "Uncategorised";

                categories.Add(new BudgetCategoryDto
                {
                    CategoryId = categoryId,
                    CategoryName = name,
                    Limit = limitAmount,
                    Spent = spent,
                    UsedPercent = PercentOf(limitAmount, spent),
                    Status = ResolveStatus(limitAmount, spent),
                    EffectiveFrom = limit?.EffectiveFrom
                });
            }

            var totalLimit = limits.Sum(l => l.MonthlyLimit);
            var totalSpent = groups.Sum(g => g.TotalAmount);
            var projected = daysElapsed > 0 ? totalSpent / daysElapsed * daysInMonth : 0;

            return new BudgetMonthDto
            {
                Month = month,
                Year = year,
                TotalLimit = totalLimit,
                TotalSpent = totalSpent,
                Projected = projected,
                UsedPercent = PercentOf(totalLimit, totalSpent),
                SafeToSpendPerDay = daysRemaining > 0
                    ? Math.Max(0, (totalLimit - totalSpent) / daysRemaining)
                    : 0,
                DaysInMonth = daysInMonth,
                DaysElapsed = daysElapsed,
                DaysRemaining = daysRemaining,
                Categories = categories
                    .OrderByDescending(c => c.Spent)
                    .ThenBy(c => c.CategoryName)
                    .ToList()
            };
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    public async Task<BudgetLimitDto?> UpsertLimitAsync(SaveBudgetLimitDto dto)
    {
        var category = await _categoryRepository.GetByIdAsync(dto.CategoryId);
        if (category is null)
        {
            return null;
        }

        var existing = (await _budgetRepository.Filter(
            x => x.CreatedBy == _currentUser.UserId && x.ExpenseCategoryId == dto.CategoryId))
            .FirstOrDefault();

        var now = DateTime.UtcNow;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        if (existing is null)
        {
            existing = new BudgetLimit
            {
                ExpenseCategoryId = dto.CategoryId,
                MonthlyLimit = dto.MonthlyLimit,
                EffectiveFrom = monthStart,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = _currentUser.UserId
            };
            await _budgetRepository.InsertAsync(existing);
        }
        else
        {
            // Ownership and the category id are server-owned; only the value changes.
            existing.MonthlyLimit = dto.MonthlyLimit;
            existing.EffectiveFrom = monthStart;
            existing.UpdatedAt = now;
            await _budgetRepository.UpdateAsync(existing);
        }

        return new BudgetLimitDto
        {
            Id = existing.Id,
            CategoryId = dto.CategoryId,
            CategoryName = category.Name,
            MonthlyLimit = existing.MonthlyLimit,
            EffectiveFrom = existing.EffectiveFrom
        };
    }

    public async Task<bool> DeleteLimitAsync(long categoryId)
    {
        var existing = (await _budgetRepository.Filter(
            x => x.CreatedBy == _currentUser.UserId && x.ExpenseCategoryId == categoryId))
            .FirstOrDefault();

        if (existing is null)
        {
            return false;
        }

        await _budgetRepository.DeleteAsync(existing.Id);
        return true;
    }

    /// <summary>
    /// How much of the month has already happened: none for a future month, all of it
    /// for a past month, today's day number for the current one.
    /// </summary>
    private static int ResolveDaysElapsed(DateTime firstDayOfMonth, int daysInMonth)
    {
        var today = DateTime.UtcNow.Date;

        if (firstDayOfMonth > today)
        {
            return 0;
        }

        if (firstDayOfMonth.AddMonths(1) <= today)
        {
            return daysInMonth;
        }

        return Math.Clamp(today.Day, 1, daysInMonth);
    }

    private static double PercentOf(double limit, double spent)
    {
        if (limit <= 0)
        {
            return 0;
        }

        return spent / limit * 100;
    }

    private static string ResolveStatus(double limit, double spent)
    {
        if (limit <= 0)
        {
            return "noBudget";
        }

        var ratio = spent / limit;
        if (ratio > 1)
        {
            return "over";
        }

        return ratio >= 0.8 ? "near" : "onTrack";
    }
}
