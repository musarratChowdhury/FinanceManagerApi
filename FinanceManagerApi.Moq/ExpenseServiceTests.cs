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
    private readonly ExpenseService _sut;
    private readonly Mock<IGenericRepository<Expense>> _expenseRepositoryMock = new();
    private readonly Mock<ILogger<ExpenseService>> _loggerMock = new();
    private readonly Mock<IMapper> _mapperMock = new();

    public ExpenseServiceTests()
    {
        _sut = new ExpenseService(_loggerMock.Object, _mapperMock.Object, _expenseRepositoryMock.Object);
    }

    [Fact]
    public async Task GetExpenseByIdAsync_ShouldReturnExpense_WhenExpenseExists()
    {
        // Arrange
        var expenseId = 1L;
        var expense = new Expense { Id = expenseId, Amount = 100, ExpenseDate = DateTime.UtcNow };
        var expenseDto = new ExpenseDto { Id = expenseId, Amount = 100, ExpenseDate = DateTime.UtcNow };

        _expenseRepositoryMock
            .Setup(repo => repo.GetByIdAsync(expenseId))
            .ReturnsAsync(expense);

        _mapperMock
            .Setup(mapper => mapper.Map<ExpenseDto>(expense))
            .Returns(expenseDto);

        // Act
        var result = await _sut.GetExpenseByIdAsync(expenseId);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expenseDto.Id, result.Id);
        Assert.Equal(expenseDto.Amount, result.Amount);
        Assert.Equal(expenseDto.ExpenseDate, result.ExpenseDate);
    }
    
    [Fact]
    public async Task GetExpenseByIdAsync_ShouldLogErrorAndThrow_WhenRepositoryThrowsException()
    {
        // Arrange
        var expenseId = 1L;
        var exception = new Exception("Database error");

        _expenseRepositoryMock
            .Setup(repo => repo.GetByIdAsync(expenseId))
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
            EntryDate = new DateTime(2024, 10, 15, 0, 0, 0, DateTimeKind.Utc)
        };
        var outOfWindowMonth = new Expense
        {
            Id = 2,
            Amount = 50,
            EntryDate = new DateTime(2024, 11, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        var outOfWindowYear = new Expense
        {
            Id = 3,
            Amount = 50,
            EntryDate = new DateTime(2023, 10, 15, 0, 0, 0, DateTimeKind.Utc)
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
        Assert.Single(result);
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