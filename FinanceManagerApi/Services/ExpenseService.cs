using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface IExpenseService
{
    Task<IEnumerable<ExpenseDto>> GetAllExpensesAsync();
    Task<ExpenseDto> GetExpenseByIdAsync(long id);
	Task<IEnumerable<ExpenseCategoryGroupDto>> GetExpensesOfThisMonthAsync();
	Task<IEnumerable<ExpenseDto>> GetExpensesOfADayAsync(DateTime day);
    Task<double> GetTotalAmountExpensesOfACategoryAsync(long categoryId);
	Task CreateExpenseAsync(ExpenseDto expenseDto);
    Task UpdateExpenseAsync(long id, ExpenseDto expenseDto);
    Task DeleteExpenseAsync(long id);
    Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryAsync(long categoryId);
    Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryThisMonthAsync(long categoryId);
    Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryADayAsync(long categoryId, DateTime day);
    Task<IEnumerable<ExpenseCategoryGroupDto>> GetExpensesOfAMonthAsync(int monthNo);
    Task<double> GetTotalExpensesOfAMonthAsync(int monthNo);
    Task<IEnumerable<MonthlyExpenseDto>> GetMonthlyExpensesForThisYearAsync();
}

public class ExpenseService : IExpenseService
{
    private readonly ILogger<ExpenseService> _logger;
    private readonly IMapper _mapper;
    private readonly IGenericRepository<Expense> _expenseRepository;
    
    public ExpenseService(ILogger<ExpenseService> logger, IMapper mapper, IGenericRepository<Expense> expenseRepository)
    {
        _logger = logger;
        _mapper = mapper;
        _expenseRepository = expenseRepository;
    }
    
    public async Task<IEnumerable<ExpenseDto>> GetAllExpensesAsync()
    {
        try
        {
            var expenses = await _expenseRepository.GetAllAsync();
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<ExpenseDto> GetExpenseByIdAsync(long id)
    {
        try
        {
            var expense = await _expenseRepository.GetByIdAsync(id);
            return _mapper.Map<ExpenseDto>(expense);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

	public async Task<IEnumerable<ExpenseCategoryGroupDto>> GetExpensesOfThisMonthAsync()
	{
		try
		{
			var now = DateTime.UtcNow;
			var firstDayOfMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
			var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

			var expenses = await _expenseRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
																x.EntryDate <= lastDayOfMonth, "ExpenseCategory");

			var expensesGroup = expenses.GroupBy(x => new

			{
				x.ExpenseCategoryId,
				x.ExpenseCategory?.Name

			}).Select(g => new ExpenseCategoryGroupDto()
			{
				CategoryId = (long)g.Key.ExpenseCategoryId,
				CategoryName = g.Key.Name,
				Expenses = _mapper.Map<IEnumerable<ExpenseDto>>(g.ToList()),
				TotalAmount = g.Sum(x=>x.Amount)
			});
			return expensesGroup;
		}
		catch (Exception e)
		{
			_logger.LogError(e, e.Message);
			throw;
		}
	}

    public async Task<IEnumerable<ExpenseCategoryGroupDto>> GetExpensesOfAMonthAsync(int monthNo)
    {
        if (monthNo < 1 || monthNo > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(monthNo), "Month number must be between 1 and 12.");
        }
        try
        {
            var now = DateTime.UtcNow;
            var firstDayOfMonth = new DateTime(now.Year, monthNo, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var expenses = await _expenseRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth, "ExpenseCategory");

            var expensesGroup = expenses.GroupBy(x => new

            {
                x.ExpenseCategoryId,
                x.ExpenseCategory?.Name

            }).Select(g => new ExpenseCategoryGroupDto()
            {
                CategoryId = (long)g.Key.ExpenseCategoryId,
                CategoryName = g.Key.Name,
                Expenses = _mapper.Map<IEnumerable<ExpenseDto>>(g.ToList()),
                TotalAmount = g.Sum(x => x.Amount)
            });
            return expensesGroup;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    public async Task<double> GetTotalExpensesOfAMonthAsync(int monthNo)
    {
        if (monthNo < 1 || monthNo > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(monthNo), "Month number must be between 1 and 12.");
        }

        try
        {
            var now = DateTime.UtcNow;
            var firstDayOfMonth = new DateTime(now.Year, monthNo, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var expenses = await _expenseRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth, "ExpenseCategory");

            var totalExpense = expenses.Sum(x => x.Amount);
            return totalExpense;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }

    public async Task<IEnumerable<MonthlyExpenseDto>> GetMonthlyExpensesForThisYearAsync()
    {
        int currentYear = DateTime.UtcNow.Year;
        var monthlyExpenses = new List<MonthlyExpenseDto>();

        for (int month = 1; month <= 12; month++)
        {
            var firstDayOfMonth = new DateTime(currentYear, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var expenses = await _expenseRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth, "ExpenseCategory");

            var totalExpense = expenses.Sum(x => x.Amount);

            monthlyExpenses.Add(new MonthlyExpenseDto
            {
                Year = currentYear,
                Month = month,
                MonthName = firstDayOfMonth.ToString("MMMM"),
                TotalExpense = totalExpense
            });
        }

        return monthlyExpenses;
    }



    public async Task<IEnumerable<ExpenseDto>> GetExpensesOfADayAsync(DateTime day)
    {
        try
        {
            var firstDayOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var expenses = await _expenseRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth);
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);

        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<double> GetTotalAmountExpensesOfACategoryAsync(long categoryId)
    {
        try
        {
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId);
            return expenses.Sum(x => x.Amount);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task CreateExpenseAsync(ExpenseDto expenseDto)
    {
        try
        {
            var expense = _mapper.Map<Expense>(expenseDto);
            await _expenseRepository.InsertAsync(expense);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    
    
    
    public async Task UpdateExpenseAsync(long id, ExpenseDto expenseDto)
    {
        try
        {
            var expense = _mapper.Map<Expense>(expenseDto);
            expense.Id = id;
            await _expenseRepository.UpdateAsync(expense);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task DeleteExpenseAsync(long id)
    {
        try
        {
            await _expenseRepository.DeleteAsync(id);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryAsync(long categoryId)
    {
        try
        {
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId);
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    
    public async Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryThisMonthAsync(long categoryId)
    {
        try
        {
            var firstDayOfMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId &&
                                                                x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth);
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryADayAsync(long categoryId, DateTime day)
    {
        try
        {
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId &&
                                                                x.EntryDate == day);
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
}