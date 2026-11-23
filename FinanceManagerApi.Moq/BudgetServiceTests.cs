using System.Linq.Expressions;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using FinanceManagerApi.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinanceManagerApi.Moq;

public class BudgetServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly BudgetService _sut;
    private readonly Mock<IGenericRepository<BudgetLimit>> _budgetRepositoryMock = new();
    private readonly Mock<IGenericRepository<ExpenseCategory>> _categoryRepositoryMock = new();
    private readonly Mock<IExpenseService> _expenseServiceMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();
    private readonly Mock<ILogger<BudgetService>> _loggerMock = new();

    public BudgetServiceTests()
    {
        _currentUserMock.Setup(user => user.UserId).Returns(OwnerId);
        _expenseServiceMock
            .Setup(service => service.GetExpensesOfAMonthAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<ExpenseCategoryGroupDto>());
        _budgetRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<BudgetLimit, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(new List<BudgetLimit>());

        _sut = new BudgetService(
            _loggerMock.Object,
            _budgetRepositoryMock.Object,
            _categoryRepositoryMock.Object,
            _expenseServiceMock.Object,
            _currentUserMock.Object);
    }

    [Fact]
    public async Task GetMonthAsync_ShouldTotalLimitsAndSpend_ForAClosedMonth()
    {
        // Arrange: a fully elapsed month, so daysRemaining is 0.
        ArrangeLimits(
            new BudgetLimit { Id = 1, ExpenseCategoryId = 10, MonthlyLimit = 1000, CreatedBy = OwnerId },
            new BudgetLimit { Id = 2, ExpenseCategoryId = 11, MonthlyLimit = 500, CreatedBy = OwnerId });
        ArrangeSpend(
            new ExpenseCategoryGroupDto { CategoryId = 10, CategoryName = "Food", TotalAmount = 1200 },
            new ExpenseCategoryGroupDto { CategoryId = 99, CategoryName = "Other", TotalAmount = 200 });

        // Act
        var result = await _sut.GetMonthAsync(1, 2024);

        // Assert
        Assert.Equal(1500, result.TotalLimit);
        Assert.Equal(1400, result.TotalSpent);
        Assert.Equal(31, result.DaysInMonth);
        Assert.Equal(31, result.DaysElapsed);
        Assert.Equal(0, result.DaysRemaining);

        // A closed month projects to exactly what was spent, and nothing is "safe" to spend.
        Assert.Equal(1400, result.Projected, 3);
        Assert.Equal(0, result.SafeToSpendPerDay, 3);
        Assert.Equal(1400d / 1500d * 100, result.UsedPercent, 3);
    }

    [Fact]
    public async Task GetMonthAsync_ShouldClassifyEachCategory()
    {
        // Arrange
        ArrangeLimits(
            new BudgetLimit { Id = 1, ExpenseCategoryId = 10, MonthlyLimit = 1000, CreatedBy = OwnerId },
            new BudgetLimit { Id = 2, ExpenseCategoryId = 11, MonthlyLimit = 500, CreatedBy = OwnerId },
            new BudgetLimit { Id = 3, ExpenseCategoryId = 12, MonthlyLimit = 200, CreatedBy = OwnerId });
        ArrangeSpend(
            new ExpenseCategoryGroupDto { CategoryId = 10, CategoryName = "Food", TotalAmount = 1200 },
            new ExpenseCategoryGroupDto { CategoryId = 11, CategoryName = "Transport", TotalAmount = 425 },
            new ExpenseCategoryGroupDto { CategoryId = 12, CategoryName = "Subscriptions", TotalAmount = 100 },
            new ExpenseCategoryGroupDto { CategoryId = 99, CategoryName = "Other", TotalAmount = 200 });

        // Act
        var result = await _sut.GetMonthAsync(1, 2024);
        var byName = result.Categories.ToDictionary(c => c.CategoryName);

        // Assert: over 100%, near 80%, on track, and a category with spend but no ceiling.
        Assert.Equal("over", byName["Food"].Status);
        Assert.Equal("near", byName["Transport"].Status);
        Assert.Equal("onTrack", byName["Subscriptions"].Status);
        Assert.Equal("noBudget", byName["Other"].Status);
        Assert.Equal(120, byName["Food"].UsedPercent, 3);
        Assert.Null(byName["Other"].EffectiveFrom);

        // Highest spend first: Food (1200), Transport (425), Other (200), Subscriptions (100).
        Assert.Equal(new[] { "Food", "Transport", "Other", "Subscriptions" },
            result.Categories.Select(c => c.CategoryName).ToArray());
    }

    [Fact]
    public async Task GetMonthAsync_ShouldProjectRunRateAndSafeToSpend_ForTheCurrentMonth()
    {
        // Arrange
        var now = DateTime.UtcNow;
        var daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
        var daysElapsed = now.Day;
        var daysRemaining = daysInMonth - daysElapsed;

        ArrangeLimits(new BudgetLimit
        {
            Id = 1,
            ExpenseCategoryId = 10,
            MonthlyLimit = 3000,
            CreatedBy = OwnerId
        });
        ArrangeSpend(new ExpenseCategoryGroupDto
        {
            CategoryId = 10,
            CategoryName = "Food",
            TotalAmount = 600
        });

        // Act
        var result = await _sut.GetMonthAsync(now.Month, now.Year);

        // Assert
        Assert.Equal(daysElapsed, result.DaysElapsed);
        Assert.Equal(daysRemaining, result.DaysRemaining);
        Assert.Equal(600d / daysElapsed * daysInMonth, result.Projected, 3);

        var expectedSafeToSpend = daysRemaining > 0
            ? Math.Max(0, (3000d - 600d) / daysRemaining)
            : 0;
        Assert.Equal(expectedSafeToSpend, result.SafeToSpendPerDay, 3);
    }

    [Fact]
    public async Task GetMonthAsync_ShouldHaveNothingSpentOrSafe_ForAFutureMonth()
    {
        // Arrange
        var startOfCurrentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var future = startOfCurrentMonth.AddMonths(2);

        ArrangeLimits(new BudgetLimit
        {
            Id = 1,
            ExpenseCategoryId = 10,
            MonthlyLimit = 3100,
            CreatedBy = OwnerId
        });

        // Act
        var result = await _sut.GetMonthAsync(future.Month, future.Year);

        // Assert
        Assert.Equal(0, result.DaysElapsed);
        Assert.Equal(DateTime.DaysInMonth(future.Year, future.Month), result.DaysRemaining);
        Assert.Equal(0, result.Projected, 3);

        // The whole ceiling is still available, spread over the month's days.
        Assert.Equal(3100d / DateTime.DaysInMonth(future.Year, future.Month), result.SafeToSpendPerDay, 3);
    }

    [Fact]
    public async Task GetMonthAsync_ShouldScopeLimitsToTheCaller()
    {
        // Arrange
        Expression<Func<BudgetLimit, bool>>? capturedPredicate = null;
        _budgetRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<BudgetLimit, bool>>>(), It.IsAny<string[]>()))
            .Callback<Expression<Func<BudgetLimit, bool>>, string[]>((p, _) => capturedPredicate = p)
            .ReturnsAsync(new List<BudgetLimit>());

        // Act
        await _sut.GetMonthAsync(1, 2024);

        // Assert
        Assert.NotNull(capturedPredicate);
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(new BudgetLimit { CreatedBy = OwnerId }));
        Assert.False(compiled(new BudgetLimit { CreatedBy = OtherId }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public async Task GetMonthAsync_ShouldThrow_WhenMonthInvalid(int month)
    {
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.GetMonthAsync(month, DateTime.UtcNow.Year));
        Assert.Equal("month", ex.ParamName);
    }

    [Fact]
    public async Task GetMonthAsync_ShouldThrow_WhenYearBeyondCurrentPlusOne()
    {
        var tooFar = DateTime.UtcNow.Year + 2;

        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => _sut.GetMonthAsync(10, tooFar));
        Assert.Equal("year", ex.ParamName);
    }

    [Fact]
    public async Task UpsertLimitAsync_ShouldCreateTheLimit_WhenTheCategoryHasNone()
    {
        // Arrange
        _categoryRepositoryMock
            .Setup(repo => repo.GetByIdAsync(10))
            .ReturnsAsync(new ExpenseCategory { Id = 10, Name = "Food" });
        _budgetRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<BudgetLimit, bool>>>()))
            .ReturnsAsync(new List<BudgetLimit>());

        BudgetLimit? inserted = null;
        _budgetRepositoryMock
            .Setup(repo => repo.InsertAsync(It.IsAny<BudgetLimit>()))
            .Callback<BudgetLimit>(limit => inserted = limit)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.UpsertLimitAsync(new SaveBudgetLimitDto { CategoryId = 10, MonthlyLimit = 1000 });

        // Assert
        Assert.NotNull(inserted);
        Assert.Equal(OwnerId, inserted!.CreatedBy);
        Assert.Equal(1000, inserted.MonthlyLimit);
        Assert.Equal(DateTime.UtcNow.Month, inserted.EffectiveFrom.Month);
        Assert.Equal("Food", result!.CategoryName);
    }

    [Fact]
    public async Task UpsertLimitAsync_ShouldUpdateInPlace_WithoutTouchingOwnership()
    {
        // Arrange
        var stored = new BudgetLimit
        {
            Id = 5,
            ExpenseCategoryId = 10,
            MonthlyLimit = 400,
            EffectiveFrom = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedBy = OwnerId
        };

        _categoryRepositoryMock
            .Setup(repo => repo.GetByIdAsync(10))
            .ReturnsAsync(new ExpenseCategory { Id = 10, Name = "Food" });
        _budgetRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<BudgetLimit, bool>>>()))
            .ReturnsAsync(new List<BudgetLimit> { stored });

        // Act
        await _sut.UpsertLimitAsync(new SaveBudgetLimitDto { CategoryId = 10, MonthlyLimit = 800 });

        // Assert
        Assert.Equal(800, stored.MonthlyLimit);
        Assert.Equal(OwnerId, stored.CreatedBy);
        Assert.Equal(DateTime.UtcNow.Month, stored.EffectiveFrom.Month);
        _budgetRepositoryMock.Verify(repo => repo.UpdateAsync(stored), Times.Once);
        _budgetRepositoryMock.Verify(repo => repo.InsertAsync(It.IsAny<BudgetLimit>()), Times.Never);
    }

    [Fact]
    public async Task UpsertLimitAsync_ShouldReturnNull_WhenTheCategoryDoesNotExist()
    {
        // Arrange
        _categoryRepositoryMock
            .Setup(repo => repo.GetByIdAsync(999))
            .ReturnsAsync((ExpenseCategory)null!);

        // Act
        var result = await _sut.UpsertLimitAsync(new SaveBudgetLimitDto { CategoryId = 999, MonthlyLimit = 100 });

        // Assert
        Assert.Null(result);
        _budgetRepositoryMock.Verify(repo => repo.InsertAsync(It.IsAny<BudgetLimit>()), Times.Never);
    }

    [Fact]
    public async Task DeleteLimitAsync_ShouldReturnFalseAndNotDelete_WhenTheCallerHasNoLimit()
    {
        // Arrange
        _budgetRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<BudgetLimit, bool>>>()))
            .ReturnsAsync(new List<BudgetLimit>());

        // Act
        var deleted = await _sut.DeleteLimitAsync(10);

        // Assert
        Assert.False(deleted);
        _budgetRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<long>()), Times.Never);
    }

    [Fact]
    public async Task DeleteLimitAsync_ShouldDeleteTheRowsOwnId_NotTheCategoryId()
    {
        // Arrange
        _budgetRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<BudgetLimit, bool>>>()))
            .ReturnsAsync(new List<BudgetLimit>
            {
                new() { Id = 77, ExpenseCategoryId = 10, MonthlyLimit = 100, CreatedBy = OwnerId }
            });

        // Act
        var deleted = await _sut.DeleteLimitAsync(10);

        // Assert
        Assert.True(deleted);
        _budgetRepositoryMock.Verify(repo => repo.DeleteAsync(77), Times.Once);
    }

    private void ArrangeLimits(params BudgetLimit[] limits)
    {
        _budgetRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<BudgetLimit, bool>>>(), It.IsAny<string[]>()))
            .ReturnsAsync(limits.ToList());
    }

    private void ArrangeSpend(params ExpenseCategoryGroupDto[] groups)
    {
        _expenseServiceMock
            .Setup(service => service.GetExpensesOfAMonthAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(groups);
    }
}
