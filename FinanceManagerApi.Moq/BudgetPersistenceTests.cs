using FinanceManagerApi.DbContext;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using FinanceManagerApi.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinanceManagerApi.Moq;

/// <summary>
/// Runs the services against a real <see cref="FinanceDbContext"/> (in-memory provider) and
/// the real <see cref="GenericRepository{TEntity}"/>, so the things Moq cannot see — the
/// "ExpenseCategory"/"Contributions" includes, the unique-per-user lookup and the mapping
/// between rows and DTOs — are exercised too.
/// </summary>
public class BudgetPersistenceTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class StubCurrentUser : ICurrentUser
    {
        public StubCurrentUser(Guid userId) => UserId = userId;
        public Guid UserId { get; }
        public bool IsAuthenticated => true;
    }

    private static FinanceDbContext CreateContext()
        => new(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase($"budgets-{Guid.NewGuid()}")
            .Options);

    private static BudgetService CreateBudgetService(
        FinanceDbContext context,
        Guid userId,
        params ExpenseCategoryGroupDto[] spend)
    {
        var expenseService = new Mock<IExpenseService>();
        expenseService
            .Setup(service => service.GetExpensesOfAMonthAsync(It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(spend);

        return new BudgetService(
            Mock.Of<ILogger<BudgetService>>(),
            new GenericRepository<BudgetLimit>(context),
            new GenericRepository<ExpenseCategory>(context),
            expenseService.Object,
            new StubCurrentUser(userId));
    }

    private static SavingsGoalService CreateGoalService(FinanceDbContext context, Guid userId)
        => new(
            Mock.Of<ILogger<SavingsGoalService>>(),
            new GenericRepository<SavingsGoal>(context),
            new GenericRepository<GoalContribution>(context),
            new StubCurrentUser(userId));

    private static async Task<ExpenseCategory> SeedCategoryAsync(FinanceDbContext context, string name)
    {
        var category = new ExpenseCategory { Name = name, EntryDate = DateTime.UtcNow, CreatedBy = OwnerId };
        context.ExpenseCategories.Add(category);
        await context.SaveChangesAsync();
        return category;
    }

    [Fact]
    public async Task UpsertLimitAsync_ShouldPersistOneRowPerCategory_AndResolveTheCategoryName()
    {
        // Arrange
        await using var context = CreateContext();
        var category = await SeedCategoryAsync(context, "Food");
        var sut = CreateBudgetService(context, OwnerId);

        // Act: save twice - the second save is an edit, not a second row.
        await sut.UpsertLimitAsync(new SaveBudgetLimitDto { CategoryId = category.Id, MonthlyLimit = 1000 });
        await sut.UpsertLimitAsync(new SaveBudgetLimitDto { CategoryId = category.Id, MonthlyLimit = 1200 });
        var summary = await sut.GetMonthAsync(DateTime.UtcNow.Month, DateTime.UtcNow.Year);

        // Assert
        Assert.Equal(1, await context.BudgetLimits.CountAsync());
        Assert.Equal(1200, summary.TotalLimit);

        // The category name comes from the ExpenseCategory include, not from a spend group.
        var row = Assert.Single(summary.Categories);
        Assert.Equal("Food", row.CategoryName);
        Assert.Equal(1200, row.Limit);
    }

    [Fact]
    public async Task GetMonthAsync_ShouldJoinSpendToTheCeiling_ByCategoryId()
    {
        // Arrange
        await using var context = CreateContext();
        var food = await SeedCategoryAsync(context, "Food");
        var other = await SeedCategoryAsync(context, "Other");
        var sut = CreateBudgetService(
            context,
            OwnerId,
            new ExpenseCategoryGroupDto { CategoryId = food.Id, CategoryName = "Food", TotalAmount = 400 },
            new ExpenseCategoryGroupDto { CategoryId = other.Id, CategoryName = "Other", TotalAmount = 250 });

        await sut.UpsertLimitAsync(new SaveBudgetLimitDto { CategoryId = food.Id, MonthlyLimit = 1000 });

        // Act
        var summary = await sut.GetMonthAsync(DateTime.UtcNow.Month, DateTime.UtcNow.Year);

        // Assert
        Assert.Equal(650, summary.TotalSpent);
        Assert.Equal(1000, summary.TotalLimit);

        var foodRow = summary.Categories.Single(c => c.CategoryId == food.Id);
        Assert.Equal(400, foodRow.Spent);
        Assert.Equal("onTrack", foodRow.Status);

        // Spend without a ceiling still appears, flagged as unbudgeted.
        var otherRow = summary.Categories.Single(c => c.CategoryId == other.Id);
        Assert.Equal("noBudget", otherRow.Status);
        Assert.Equal(0, otherRow.Limit);
    }

    [Fact]
    public async Task DeleteLimitAsync_ShouldOnlyRemoveTheCallersRow()
    {
        // Arrange
        await using var context = CreateContext();
        var category = await SeedCategoryAsync(context, "Food");
        var ownerService = CreateBudgetService(context, OwnerId);
        var otherService = CreateBudgetService(context, OtherId);

        await ownerService.UpsertLimitAsync(new SaveBudgetLimitDto { CategoryId = category.Id, MonthlyLimit = 900 });

        // Act & assert: a second user cannot see or delete the first user's ceiling.
        var otherSummary = await otherService.GetMonthAsync(DateTime.UtcNow.Month, DateTime.UtcNow.Year);
        Assert.Equal(0, otherSummary.TotalLimit);
        Assert.False(await otherService.DeleteLimitAsync(category.Id));

        // ...but the owner can.
        Assert.True(await ownerService.DeleteLimitAsync(category.Id));
        Assert.Equal(0, await context.BudgetLimits.CountAsync());
    }

    [Fact]
    public async Task Contributions_ShouldAccumulateOnTheGoal_AndSurviveAReRead()
    {
        // Arrange
        await using var context = CreateContext();
        var sut = CreateGoalService(context, OwnerId);

        var created = await sut.CreateAsync(new CreateSavingsGoalDto
        {
            Name = "new pc",
            TargetAmount = 100000,
            TargetDate = DateTime.UtcNow.AddMonths(12),
            InitialSaved = 5000
        });

        await sut.AddContributionAsync(created.Id, new AddGoalContributionDto { Amount = 2500 });

        // Act: a fresh read, so the Contributions include is exercised.
        var goals = (await sut.GetAllAsync()).ToList();

        // Assert
        var goal = Assert.Single(goals);
        Assert.Equal(7500, goal.SavedAmount);
        Assert.Equal(2, goal.Contributions.Count());
        Assert.Equal(2, await context.GoalContributions.CountAsync());
        Assert.Equal(100000 - 7500, (goal.NeededPerMonth ?? 0) * (goal.MonthsRemaining ?? 1), 3);
    }

    [Fact]
    public async Task Goals_ShouldBeIsolatedPerUser()
    {
        // Arrange
        await using var context = CreateContext();
        var ownerService = CreateGoalService(context, OwnerId);
        var otherService = CreateGoalService(context, OtherId);

        var goal = await ownerService.CreateAsync(new CreateSavingsGoalDto { Name = "bike", TargetAmount = 5000 });

        // Act & assert
        Assert.Empty(await otherService.GetAllAsync());
        Assert.Null(await otherService.GetByIdAsync(goal.Id));
        Assert.False(await otherService.DeleteAsync(goal.Id));
        Assert.Null(await otherService.AddContributionAsync(goal.Id, new AddGoalContributionDto { Amount = 10 }));

        // The owner still has it, untouched.
        Assert.Single(await ownerService.GetAllAsync());
    }

    [Fact]
    public async Task DeleteGoal_ShouldRemoveTheGoalItself()
    {
        // Arrange
        await using var context = CreateContext();
        var sut = CreateGoalService(context, OwnerId);
        var goal = await sut.CreateAsync(new CreateSavingsGoalDto { Name = "trip", TargetAmount = 1000 });

        // Act
        var deleted = await sut.DeleteAsync(goal.Id);

        // Assert
        Assert.True(deleted);
        Assert.Empty(await context.SavingsGoals.ToListAsync());
    }
}
