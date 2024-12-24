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


}