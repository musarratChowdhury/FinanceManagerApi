using System.Linq.Expressions;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using FinanceManagerApi.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinanceManagerApi.Moq;

public class SavingsGoalServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly SavingsGoalService _sut;
    private readonly Mock<IGenericRepository<SavingsGoal>> _goalRepositoryMock = new();
    private readonly Mock<IGenericRepository<GoalContribution>> _contributionRepositoryMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<ILogger<SavingsGoalService>> _loggerMock = new();

    public SavingsGoalServiceTests()
    {
        _currentUserMock.Setup(user => user.UserId).Returns(OwnerId);
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(new List<SavingsGoal>());
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>()))
            .ReturnsAsync(new List<SavingsGoal>());
        _contributionRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<GoalContribution, bool>>>()))
            .ReturnsAsync(new List<GoalContribution>());

        _sut = new SavingsGoalService(
            _loggerMock.Object,
            _goalRepositoryMock.Object,
            _contributionRepositoryMock.Object,
            _currentUserMock.Object);
    }

    [Fact]
    public async Task GetAllAsync_ShouldSumTheLedger_AndDeriveAverageAndPace()
    {
        // Arrange: 5000 saved over three months, 100000 target in 10 months.
        var now = DateTime.UtcNow;
        var goal = new SavingsGoal
        {
            Id = 1,
            Name = "new pc",
            TargetAmount = 100000,
            TargetDate = now.AddMonths(10),
            CreatedAt = now.AddMonths(-2),
            CreatedBy = OwnerId,
            Contributions = new List<GoalContribution>
            {
                new() { Id = 1, SavingsGoalId = 1, Amount = 2000, ContributionDate = now.AddMonths(-2), CreatedBy = OwnerId },
                new() { Id = 2, SavingsGoalId = 1, Amount = 1000, ContributionDate = now.AddMonths(-1), CreatedBy = OwnerId },
                new() { Id = 3, SavingsGoalId = 1, Amount = 2000, ContributionDate = now, CreatedBy = OwnerId }
            }
        };

        ArrangeGoals(goal);

        // Act
        var result = (await _sut.GetAllAsync()).Single();

        // Assert
        Assert.Equal(5000, result.SavedAmount);
        Assert.Equal(3, result.Contributions.Count());

        // 5000 spread over the three months the money has had to accumulate.
        Assert.Equal(5000d / 3d, result.AvgSavedPerMonth, 3);

        // (100000 - 5000) over the ten months left.
        Assert.Equal(9500, result.NeededPerMonth!.Value, 3);
        Assert.Equal(10, result.MonthsRemaining);

        // ~57 months of saving at the current average is far past the target date.
        Assert.Equal("late", result.PaceStatus);
        Assert.NotNull(result.ProjectedReachDate);

        // Newest contribution first.
        Assert.Equal(new[] { 3L, 2L, 1L }, result.Contributions.Select(c => c.Id).ToArray());
    }

    [Fact]
    public async Task GetAllAsync_ShouldReportOnTrack_WhenTheAverageBeatsTheDeadline()
    {
        // Arrange: 6000 of 10000 in three months against a six-month deadline.
        var now = DateTime.UtcNow;
        var goal = new SavingsGoal
        {
            Id = 2,
            Name = "trip",
            TargetAmount = 10000,
            TargetDate = now.AddMonths(6),
            CreatedBy = OwnerId,
            Contributions = new List<GoalContribution>
            {
                new() { Id = 1, SavingsGoalId = 2, Amount = 6000, ContributionDate = now.AddMonths(-2), CreatedBy = OwnerId }
            }
        };

        ArrangeGoals(goal);

        // Act
        var result = (await _sut.GetAllAsync()).Single();

        // Assert: 2000/month clears the remaining 4000 in two months, well inside six.
        Assert.Equal(2000, result.AvgSavedPerMonth, 3);
        Assert.Equal("onTrack", result.PaceStatus);
    }

    [Fact]
    public async Task GetAllAsync_ShouldHandleGoalsWithoutSavingsOrWithoutADate()
    {
        // Arrange
        var noSavings = new SavingsGoal
        {
            Id = 1,
            Name = "bike",
            TargetAmount = 5000,
            TargetDate = DateTime.UtcNow.AddMonths(4),
            CreatedBy = OwnerId
        };
        var noDate = new SavingsGoal
        {
            Id = 2,
            Name = "emergency fund",
            TargetAmount = 5000,
            CreatedBy = OwnerId,
            Contributions = new List<GoalContribution>
            {
                new() { Id = 9, SavingsGoalId = 2, Amount = 500, ContributionDate = DateTime.UtcNow, CreatedBy = OwnerId }
            }
        };

        ArrangeGoals(noDate, noSavings);

        // Act
        var results = (await _sut.GetAllAsync()).ToList();

        // Assert
        var bike = results.Single(g => g.Name == "bike");
        Assert.Equal("noSavings", bike.PaceStatus);
        Assert.Equal(0, bike.AvgSavedPerMonth, 3);
        Assert.Equal(0, bike.SavedAmount, 3);
        Assert.Equal(1250, bike.NeededPerMonth!.Value, 3);

        var fund = results.Single(g => g.Name == "emergency fund");
        Assert.Equal("noTarget", fund.PaceStatus);
        Assert.Null(fund.NeededPerMonth);
        Assert.Null(fund.MonthsRemaining);
    }

    [Fact]
    public async Task GetAllAsync_ShouldReportReached_WhenSavedCoversTheTarget()
    {
        // Arrange
        var goal = new SavingsGoal
        {
            Id = 3,
            Name = "laptop",
            TargetAmount = 1000,
            TargetDate = DateTime.UtcNow.AddMonths(2),
            CreatedBy = OwnerId,
            Contributions = new List<GoalContribution>
            {
                new() { Id = 1, SavingsGoalId = 3, Amount = 1500, ContributionDate = DateTime.UtcNow, CreatedBy = OwnerId }
            }
        };

        ArrangeGoals(goal);

        // Act
        var result = (await _sut.GetAllAsync()).Single();

        // Assert
        Assert.Equal("reached", result.PaceStatus);
        Assert.Equal(0, result.NeededPerMonth!.Value, 3);
    }

    [Fact]
    public async Task GetAllAsync_ShouldScopeGoalsToTheCaller()
    {
        // Arrange
        Expression<Func<SavingsGoal, bool>>? capturedPredicate = null;
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>(), It.IsAny<string[]>()))
            .Callback<Expression<Func<SavingsGoal, bool>>, string[]>((p, _) => capturedPredicate = p)
            .ReturnsAsync(new List<SavingsGoal>());

        // Act
        await _sut.GetAllAsync();

        // Assert
        Assert.NotNull(capturedPredicate);
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(new SavingsGoal { CreatedBy = OwnerId }));
        Assert.False(compiled(new SavingsGoal { CreatedBy = OtherId }));
    }

    [Fact]
    public async Task GetByIdAsync_ShouldReturnNull_WhenTheGoalBelongsToSomebodyElse()
    {
        // Arrange
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(new List<SavingsGoal>());

        // Act
        var result = await _sut.GetByIdAsync(42);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task CreateAsync_ShouldRecordTheOpeningBalanceAsAContribution()
    {
        // Arrange
        GoalContribution? inserted = null;
        _goalRepositoryMock
            .Setup(repo => repo.InsertAsync(It.IsAny<SavingsGoal>()))
            .Callback<SavingsGoal>(goal => goal.Id = 55)
            .Returns(Task.CompletedTask);
        _contributionRepositoryMock
            .Setup(repo => repo.InsertAsync(It.IsAny<GoalContribution>()))
            .Callback<GoalContribution>(contribution => inserted = contribution)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.CreateAsync(new CreateSavingsGoalDto
        {
            Name = "new pc",
            TargetAmount = 100000,
            TargetDate = DateTime.UtcNow.AddMonths(12),
            InitialSaved = 5000
        });

        // Assert
        Assert.Equal(55, result.Id);
        Assert.NotNull(inserted);
        Assert.Equal(55, inserted!.SavingsGoalId);
        Assert.Equal(5000, inserted.Amount);
        Assert.Equal(OwnerId, inserted.CreatedBy);
        Assert.Equal(5000, result.SavedAmount);
    }

    [Fact]
    public async Task CreateAsync_ShouldNotWriteAContribution_WhenNothingIsSavedYet()
    {
        // Act
        await _sut.CreateAsync(new CreateSavingsGoalDto { Name = "bike", TargetAmount = 5000 });

        // Assert
        _contributionRepositoryMock.Verify(repo => repo.InsertAsync(It.IsAny<GoalContribution>()), Times.Never);
    }

    [Fact]
    public async Task AddContributionAsync_ShouldReturnNull_WhenTheGoalIsNotTheCallers()
    {
        // Arrange
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(new List<SavingsGoal>());

        // Act
        var result = await _sut.AddContributionAsync(42, new AddGoalContributionDto { Amount = 100 });

        // Assert
        Assert.Null(result);
        _contributionRepositoryMock.Verify(repo => repo.InsertAsync(It.IsAny<GoalContribution>()), Times.Never);
    }

    [Fact]
    public async Task AddContributionAsync_ShouldPinAnUnspecifiedDateToUtc()
    {
        // Arrange: a bare "2026-11-01" deserialises with Kind=Unspecified, which Npgsql rejects.
        var goal = new SavingsGoal
        {
            Id = 7,
            Name = "pc",
            TargetAmount = 1000,
            CreatedBy = OwnerId,
            Contributions = new List<GoalContribution>()
        };
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(new List<SavingsGoal> { goal });

        GoalContribution? inserted = null;
        _contributionRepositoryMock
            .Setup(repo => repo.InsertAsync(It.IsAny<GoalContribution>()))
            .Callback<GoalContribution>(contribution => inserted = contribution)
            .Returns(Task.CompletedTask);

        // Act
        await _sut.AddContributionAsync(7, new AddGoalContributionDto
        {
            Amount = 250,
            ContributionDate = new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Unspecified)
        });

        // Assert
        Assert.NotNull(inserted);
        Assert.Equal(DateTimeKind.Utc, inserted!.ContributionDate.Kind);
        Assert.Equal(new DateTime(2026, 11, 1), inserted.ContributionDate);
    }

    [Fact]
    public async Task DeleteContributionAsync_ShouldReturnFalse_WhenTheGoalIsNotTheCallers()
    {
        // Arrange
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>()))
            .ReturnsAsync(new List<SavingsGoal>());

        // Act
        var deleted = await _sut.DeleteContributionAsync(42, 1);

        // Assert
        Assert.False(deleted);
        _contributionRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task DeleteContributionAsync_ShouldDeleteTheRowOwnedByTheGoal()
    {
        // Arrange
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>()))
            .ReturnsAsync(new List<SavingsGoal> { new() { Id = 7, CreatedBy = OwnerId } });
        _contributionRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<GoalContribution, bool>>>()))
            .ReturnsAsync(new List<GoalContribution> { new() { Id = 3, SavingsGoalId = 7, Amount = 100 } });

        // Act
        var deleted = await _sut.DeleteContributionAsync(7, 3);

        // Assert
        Assert.True(deleted);
        _contributionRepositoryMock.Verify(repo => repo.DeleteAsync(3), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_ShouldReturnFalseAndNotDelete_WhenTheGoalIsNotTheCallers()
    {
        // Arrange
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>()))
            .ReturnsAsync(new List<SavingsGoal>());

        // Act
        var deleted = await _sut.DeleteAsync(42);

        // Assert
        Assert.False(deleted);
        _goalRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<long>()), Times.Never);
    }

    private void ArrangeGoals(params SavingsGoal[] goals)
    {
        _goalRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<SavingsGoal, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(goals.ToList());
    }
}
