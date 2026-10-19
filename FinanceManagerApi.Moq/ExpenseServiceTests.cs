using System.Linq.Expressions;
using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using FinanceManagerApi.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace FinanceManagerApi.Moq;

public class ExpenseServiceTests
{
    private static readonly Guid OwnerId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly ExpenseService _sut;
    private readonly Mock<IGenericRepository<Expense>> _expenseRepositoryMock = new();
    private readonly Mock<ILogger<ExpenseService>> _loggerMock = new();
    private readonly Mock<IMapper> _mapperMock = new();
    private readonly Mock<ICurrentUser> _currentUserMock = new();

    public ExpenseServiceTests()
    {
        _currentUserMock.Setup(user => user.UserId).Returns(OwnerId);
        _sut = new ExpenseService(_loggerMock.Object, _mapperMock.Object, _expenseRepositoryMock.Object, _currentUserMock.Object);
    }

    [Fact]
    public async Task GetExpenseByIdAsync_ShouldReturnExpense_WhenExpenseExists()
    {
        // Arrange
        var expenseId = 1L;
        var expense = new Expense { Id = expenseId, Amount = 100, CreatedBy = OwnerId, ExpenseDate = DateTime.UtcNow };
        var expenseDto = new ExpenseDto { Id = expenseId, Amount = 100, ExpenseDate = DateTime.UtcNow };

        Expression<Func<Expense, bool>>? capturedPredicate = null;
        _expenseRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<Expense, bool>>>()))
            .Callback<Expression<Func<Expense, bool>>>(p => capturedPredicate = p)
            .ReturnsAsync(new List<Expense> { expense });

        _mapperMock
            .Setup(mapper => mapper.Map<ExpenseDto>(expense))
            .Returns(expenseDto);

        // Act
        var result = await _sut.GetExpenseByIdAsync(expenseId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expenseDto.Id, result!.Id);
        Assert.Equal(expenseDto.Amount, result.Amount);
        Assert.Equal(expenseDto.ExpenseDate, result.ExpenseDate);

        // The lookup must also be scoped to the caller, so the same id owned by somebody else - or
        // a different id owned by the caller - cannot satisfy it.
        Assert.NotNull(capturedPredicate);
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(expense));
        Assert.False(compiled(new Expense { Id = expenseId, Amount = 100, CreatedBy = OtherId }));
        Assert.False(compiled(new Expense { Id = expenseId + 1, Amount = 100, CreatedBy = OwnerId }));
    }

    [Fact]
    public async Task GetExpenseByIdAsync_ShouldReturnNull_WhenExpenseBelongsToAnotherUser()
    {
        // Arrange
        _expenseRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<Expense, bool>>>()))
            .ReturnsAsync(new List<Expense>());

        // Act
        var result = await _sut.GetExpenseByIdAsync(42L);

        // Assert
        Assert.Null(result);
        _mapperMock.Verify(mapper => mapper.Map<ExpenseDto>(It.IsAny<object>()), Times.Never);
    }

    [Fact]
    public async Task GetExpenseByIdAsync_ShouldLogErrorAndThrow_WhenRepositoryThrowsException()
    {
        // Arrange
        var expenseId = 1L;
        var exception = new Exception("Database error");

        _expenseRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<Expense, bool>>>()))
            .ThrowsAsync(exception);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<Exception>(() => _sut.GetExpenseByIdAsync(expenseId));
        Assert.Equal("Database error", ex.Message);

        // Verify logging
        _loggerMock.Verify(
            logger => logger.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString().Contains("Database error")),
                exception,
                It.IsAny<Func<It.IsAnyType, Exception, string>>()
            ),
            Times.Once
        );
    }

    [Fact]
    public async Task GetExpensesOfAMonthAsync_ShouldUseSuppliedYearAndMonth()
    {
        // Arrange
        var month = 10;
        var year = 2024;
        var inWindow = new Expense
        {
            Id = 1,
            Amount = 50,
            CreatedBy = OwnerId,
            EntryDate = new DateTime(2024, 10, 15, 0, 0, 0, DateTimeKind.Utc)
        };
        var outOfWindowMonth = new Expense
        {
            Id = 2,
            Amount = 50,
            CreatedBy = OwnerId,
            EntryDate = new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var outOfWindowYear = new Expense
        {
            Id = 3,
            Amount = 50,
            CreatedBy = OwnerId,
            EntryDate = new DateTime(2023, 10, 15, 0, 0, 0, DateTimeKind.Utc)
        };
        var inWindowButAnotherUser = new Expense
        {
            Id = 4,
            Amount = 50,
            CreatedBy = OtherId,
            EntryDate = new DateTime(2024, 10, 15, 0, 0, 0, DateTimeKind.Utc)
        };

        Expression<Func<Expense, bool>>? capturedPredicate = null;

        _expenseRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<Expense, bool>>>(), "ExpenseCategory"))
            .Callback<Expression<Func<Expense, bool>>, string>((p, _) => capturedPredicate = p)
            .ReturnsAsync(new List<Expense> { inWindow });

        _mapperMock
            .Setup(mapper => mapper.Map<IEnumerable<ExpenseDto>>(It.IsAny<IEnumerable<Expense>>()))
            .Returns((IEnumerable<Expense> src) => src.Select(e => new ExpenseDto { Id = e.Id, Amount = e.Amount }).ToList());

        // Act
        var result = await _sut.GetExpensesOfAMonthAsync(month, year);

        // Assert
        Assert.NotNull(capturedPredicate);
        var compiled = capturedPredicate!.Compile();
        Assert.True(compiled(inWindow));
        Assert.False(compiled(outOfWindowMonth));
        Assert.False(compiled(outOfWindowYear));
        Assert.False(compiled(inWindowButAnotherUser));
        Assert.Single(result);
    }

    [Fact]
    public async Task UpdateExpenseAsync_ShouldPreserveServerOwnedFields_WhenExpenseExists()
    {
        // Arrange
        var entryDate = new DateTime(2024, 1, 5, 0, 0, 0, DateTimeKind.Utc);
        var stored = new Expense
        {
            Id = 7,
            Cause = "before",
            Amount = 5,
            CreatedBy = OwnerId,
            EntryDate = entryDate
        };

        _expenseRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<Expense, bool>>>()))
            .ReturnsAsync(new List<Expense> { stored });

        // A hostile payload: claims somebody else's id and a fresh EntryDate.
        var payload = new ExpenseDto
        {
            Id = 7,
            Cause = "after",
            Amount = 42,
            CreatedBy = OtherId,
            EntryDate = new DateTime(2025, 6, 6, 0, 0, 0, DateTimeKind.Utc)
        };

        // Act
        var updated = await _sut.UpdateExpenseAsync(7, payload);

        // Assert
        Assert.True(updated);
        Assert.Equal("after", stored.Cause);
        Assert.Equal(42, stored.Amount);
        Assert.Equal(OwnerId, stored.CreatedBy);
        Assert.Equal(entryDate, stored.EntryDate);
        _expenseRepositoryMock.Verify(repo => repo.UpdateAsync(stored), Times.Once);
    }

    [Fact]
    public async Task UpdateExpenseAsync_ShouldReturnFalseAndNotSave_WhenExpenseBelongsToAnotherUser()
    {
        // Arrange
        _expenseRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<Expense, bool>>>()))
            .ReturnsAsync(new List<Expense>());

        // Act
        var updated = await _sut.UpdateExpenseAsync(99L, new ExpenseDto { Id = 99L, Amount = 10 });

        // Assert
        Assert.False(updated);
        _expenseRepositoryMock.Verify(repo => repo.UpdateAsync(It.IsAny<Expense>()), Times.Never);
    }

    [Fact]
    public async Task DeleteExpenseAsync_ShouldReturnFalseAndNotDelete_WhenExpenseBelongsToAnotherUser()
    {
        // Arrange
        _expenseRepositoryMock
            .Setup(repo => repo.Filter(It.IsAny<Expression<Func<Expense, bool>>>()))
            .ReturnsAsync(new List<Expense>());

        // Act
        var deleted = await _sut.DeleteExpenseAsync(99L);

        // Assert
        Assert.False(deleted);
        _expenseRepositoryMock.Verify(repo => repo.DeleteAsync(It.IsAny<long>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(13)]
    [InlineData(-1)]
    public async Task GetExpensesOfAMonthAsync_ShouldThrowArgumentOutOfRangeException_WhenMonthInvalid(int month)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.GetExpensesOfAMonthAsync(month, DateTime.UtcNow.Year));
        Assert.Equal("monthNo", ex.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetExpensesOfAMonthAsync_ShouldThrowArgumentOutOfRangeException_WhenYearTooSmall(int year)
    {
        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.GetExpensesOfAMonthAsync(10, year));
        Assert.Equal("year", ex.ParamName);
    }

    [Fact]
    public async Task GetExpensesOfAMonthAsync_ShouldThrowArgumentOutOfRangeException_WhenYearBeyondCurrentPlusOne()
    {
        // Arrange
        var currentYear = DateTime.UtcNow.Year;
        var tooFar = currentYear + 2;

        // Act & Assert
        var ex = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _sut.GetExpensesOfAMonthAsync(10, tooFar));
        Assert.Equal("year", ex.ParamName);
    }
}
