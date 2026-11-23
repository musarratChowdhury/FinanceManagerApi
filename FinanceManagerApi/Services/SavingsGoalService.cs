using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface ISavingsGoalService
{
    Task<IEnumerable<SavingsGoalDto>> GetAllAsync();
    Task<SavingsGoalDto?> GetByIdAsync(long id);
    Task<SavingsGoalDto> CreateAsync(CreateSavingsGoalDto dto);
    Task<SavingsGoalDto?> UpdateAsync(long id, UpdateSavingsGoalDto dto);
    Task<bool> DeleteAsync(long id);
    Task<SavingsGoalDto?> AddContributionAsync(long goalId, AddGoalContributionDto dto);
    Task<bool> DeleteContributionAsync(long goalId, long contributionId);
}

/// <summary>
/// Savings goals backed by a contribution ledger.
///
/// "Saved so far" is the sum of the goal's contributions, so the average-per-month and
/// the pace verdict come from real history. A wrong entry is deleted, never edited, which
/// is what keeps those two figures trustworthy.
///
/// Every read is scoped to the caller; a goal owned by somebody else answers "not found"
/// rather than "forbidden", so ids cannot be probed.
/// </summary>
public class SavingsGoalService : ISavingsGoalService
{
    private readonly ILogger<SavingsGoalService> _logger;
    private readonly IGenericRepository<SavingsGoal> _goalRepository;
    private readonly IGenericRepository<GoalContribution> _contributionRepository;
    private readonly ICurrentUser _currentUser;

    public SavingsGoalService(
        ILogger<SavingsGoalService> logger,
        IGenericRepository<SavingsGoal> goalRepository,
        IGenericRepository<GoalContribution> contributionRepository,
        ICurrentUser currentUser)
    {
        _logger = logger;
        _goalRepository = goalRepository;
        _contributionRepository = contributionRepository;
        _currentUser = currentUser;
    }

    public async Task<IEnumerable<SavingsGoalDto>> GetAllAsync()
    {
        var goals = await _goalRepository.Filter(
            x => x.CreatedBy == _currentUser.UserId,
            "Contributions");

        var now = DateTime.UtcNow;
        return goals
            .OrderBy(g => g.TargetDate ?? DateTime.MaxValue)
            .ThenBy(g => g.Name)
            .Select(g => ToDto(g, now))
            .ToList();
    }

    public async Task<SavingsGoalDto?> GetByIdAsync(long id)
    {
        var goal = (await _goalRepository.Filter(
            x => x.Id == id && x.CreatedBy == _currentUser.UserId,
            "Contributions"))
            .FirstOrDefault();

        return goal is null ? null : ToDto(goal, DateTime.UtcNow);
    }

    public async Task<SavingsGoalDto> CreateAsync(CreateSavingsGoalDto dto)
    {
        var now = DateTime.UtcNow;

        var goal = new SavingsGoal
        {
            Name = dto.Name.Trim(),
            TargetAmount = dto.TargetAmount,
            TargetDate = NormalizeToUtc(dto.TargetDate),
            CreatedAt = now,
            CreatedBy = _currentUser.UserId
        };

        await _goalRepository.InsertAsync(goal);

        // The opening balance is a contribution like any other, so the ledger is the
        // single source of truth from day one.
        var initial = dto.InitialSaved ?? 0;
        if (initial > 0)
        {
            var contribution = new GoalContribution
            {
                SavingsGoalId = goal.Id,
                Amount = initial,
                ContributionDate = now.Date,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            };

            await _contributionRepository.InsertAsync(contribution);

            // EF's relationship fix-up already put the row on the tracked goal; Add only
            // when it is missing (a mocked repository does no fix-up) so the collection
            // can never hold the same contribution twice and inflate the total.
            if (!goal.Contributions.Contains(contribution))
            {
                goal.Contributions.Add(contribution);
            }
        }

        return ToDto(goal, now);
    }

    public async Task<SavingsGoalDto?> UpdateAsync(long id, UpdateSavingsGoalDto dto)
    {
        var goal = (await _goalRepository.Filter(
            x => x.Id == id && x.CreatedBy == _currentUser.UserId,
            "Contributions"))
            .FirstOrDefault();

        if (goal is null)
        {
            return null;
        }

        // Allow-list: ownership, created date and contributions stay server-owned.
        goal.Name = dto.Name.Trim();
        goal.TargetAmount = dto.TargetAmount;
        goal.TargetDate = NormalizeToUtc(dto.TargetDate);

        await _goalRepository.UpdateAsync(goal);

        return ToDto(goal, DateTime.UtcNow);
    }

    public async Task<bool> DeleteAsync(long id)
    {
        var goal = (await _goalRepository.Filter(
            x => x.Id == id && x.CreatedBy == _currentUser.UserId))
            .FirstOrDefault();

        if (goal is null)
        {
            return false;
        }

        // Contributions are cascade-deleted by the database.
        await _goalRepository.DeleteAsync(goal.Id);
        return true;
    }

    public async Task<SavingsGoalDto?> AddContributionAsync(long goalId, AddGoalContributionDto dto)
    {
        var goal = (await _goalRepository.Filter(
            x => x.Id == goalId && x.CreatedBy == _currentUser.UserId,
            "Contributions"))
            .FirstOrDefault();

        if (goal is null)
        {
            return null;
        }

        var now = DateTime.UtcNow;
        var contribution = new GoalContribution
        {
            SavingsGoalId = goal.Id,
            Amount = dto.Amount,
            ContributionDate = NormalizeToUtc(dto.ContributionDate) ?? now.Date,
            CreatedAt = now,
            CreatedBy = _currentUser.UserId
        };

        await _contributionRepository.InsertAsync(contribution);

        // Same guard as CreateAsync: fix-up may have already attached it, and a duplicate
        // entry in the collection would double the amount shown straight after saving.
        if (!goal.Contributions.Contains(contribution))
        {
            goal.Contributions.Add(contribution);
        }

        return ToDto(goal, now);
    }

    public async Task<bool> DeleteContributionAsync(long goalId, long contributionId)
    {
        // Ownership is checked on the goal, not the contribution: a contribution can
        // never belong to a goal the caller cannot see.
        var goal = (await _goalRepository.Filter(
            x => x.Id == goalId && x.CreatedBy == _currentUser.UserId))
            .FirstOrDefault();

        if (goal is null)
        {
            return false;
        }

        var contribution = (await _contributionRepository.Filter(
            x => x.Id == contributionId && x.SavingsGoalId == goalId))
            .FirstOrDefault();

        if (contribution is null)
        {
            return false;
        }

        await _contributionRepository.DeleteAsync(contribution.Id);
        return true;
    }

    private SavingsGoalDto ToDto(SavingsGoal goal, DateTime now)
    {
        var contributions = (goal.Contributions ?? new List<GoalContribution>())
            .OrderByDescending(c => c.ContributionDate)
            .ToList();

        var saved = contributions.Sum(c => c.Amount);
        var firstContribution = contributions.Count > 0
            ? contributions.Min(c => c.ContributionDate)
            : (DateTime?)null;

        // Months the money has had to accumulate: at least one, so a goal funded today
        // reports its own amount rather than dividing by zero.
        var monthsElapsed = firstContribution is null
            ? 1
            : Math.Max(1, MonthsBetween(firstContribution.Value, now) + 1);

        var avgSavedPerMonth = saved / monthsElapsed;

        int? monthsRemaining = null;
        double? neededPerMonth = null;
        if (goal.TargetDate is not null)
        {
            monthsRemaining = Math.Max(0, MonthsBetween(now, goal.TargetDate.Value));
            var remaining = Math.Max(0, goal.TargetAmount - saved);
            neededPerMonth = monthsRemaining == 0 ? remaining : remaining / monthsRemaining.Value;
        }

        DateTime? projectedReachDate = null;
        string paceStatus;

        if (goal.TargetAmount > 0 && saved >= goal.TargetAmount)
        {
            paceStatus = "reached";
        }
        else if (saved <= 0)
        {
            paceStatus = "noSavings";
        }
        else if (goal.TargetDate is null)
        {
            paceStatus = "noTarget";
        }
        else
        {
            var remaining = Math.Max(0, goal.TargetAmount - saved);
            var monthsNeeded = avgSavedPerMonth > 0
                ? (int)Math.Ceiling(remaining / avgSavedPerMonth)
                : int.MaxValue;

            if (avgSavedPerMonth > 0)
            {
                projectedReachDate = now.AddMonths(Math.Max(0, monthsNeeded));
            }

            paceStatus = monthsNeeded <= monthsRemaining ? "onTrack" : "late";
        }

        return new SavingsGoalDto
        {
            Id = goal.Id,
            Name = goal.Name,
            TargetAmount = goal.TargetAmount,
            SavedAmount = saved,
            TargetDate = goal.TargetDate,
            CreatedAt = goal.CreatedAt,
            AvgSavedPerMonth = avgSavedPerMonth,
            NeededPerMonth = neededPerMonth,
            MonthsRemaining = monthsRemaining,
            PaceStatus = paceStatus,
            ProjectedReachDate = projectedReachDate,
            Contributions = contributions.Select(c => new GoalContributionDto
            {
                Id = c.Id,
                Amount = c.Amount,
                ContributionDate = c.ContributionDate
            }).ToList()
        };
    }

    private static int MonthsBetween(DateTime from, DateTime to)
        => (to.Year - from.Year) * 12 + (to.Month - from.Month);

    /// <summary>
    /// Npgsql refuses to write a DateTime whose Kind is not Utc into a timestamptz column,
    /// so a date that arrives without an offset is pinned to UTC midnight.
    /// </summary>
    private static DateTime? NormalizeToUtc(DateTime? value)
    {
        if (value is null)
        {
            return null;
        }

        return value.Value.Kind switch
        {
            DateTimeKind.Utc => value.Value,
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)
        };
    }
}
