using AutoMapper;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;

namespace FinanceManagerApi.Services;

public interface IExpenseService
{
    Task<IEnumerable<ExpenseDto>> GetAllExpensesAsync();
    Task<ExpenseDto?> GetExpenseByIdAsync(long id);
	Task<IEnumerable<ExpenseCategoryGroupDto>> GetExpensesOfThisMonthAsync();
	Task<IEnumerable<ExpenseDto>> GetExpensesOfADayAsync(DateTime day);
    Task<double> GetTotalAmountExpensesOfACategoryAsync(long categoryId);
	Task CreateExpenseAsync(ExpenseDto expenseDto);
    Task<bool> UpdateExpenseAsync(long id, ExpenseDto expenseDto);
    Task<bool> DeleteExpenseAsync(long id);
    Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryAsync(long categoryId);
    Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryThisMonthAsync(long categoryId);
    Task<IEnumerable<ExpenseDto>> GetExpensesOfACategoryADayAsync(long categoryId, DateTime day);
    Task<IEnumerable<ExpenseCategoryGroupDto>> GetExpensesOfAMonthAsync(int monthNo, int year);
    Task<double> GetTotalExpensesOfAMonthAsync(int monthNo);
    Task<IEnumerable<MonthlyExpenseDto>> GetMonthlyExpensesForThisYearAsync();
}

// Month windows are matched on ExpenseDate (when the spend actually happened) and fall back to
// EntryDate for legacy rows that never had one. Both are non-null-only inside the DB as
// COALESCE(ExpenseDate, EntryDate), so keep the (x.ExpenseDate ?? x.EntryDate) form.
//
// Every query below is also scoped to CreatedBy == the caller: an expense belongs to the user who
// entered it, so a new read that omits "x.CreatedBy == _currentUser.UserId" silently leaks
// another account's rows.
public class ExpenseService : IExpenseService
{
    private readonly ILogger<ExpenseService> _logger;
    private readonly IMapper _mapper;
    private readonly IGenericRepository<Expense> _expenseRepository;
    private readonly ICurrentUser _currentUser;

    public ExpenseService(ILogger<ExpenseService> logger, IMapper mapper, IGenericRepository<Expense> expenseRepository, ICurrentUser currentUser)
    {
        _logger = logger;
        _mapper = mapper;
        _expenseRepository = expenseRepository;
        _currentUser = currentUser;
    }
    
    public async Task<IEnumerable<ExpenseDto>> GetAllExpensesAsync()
    {
        try
        {
            var expenses = await _expenseRepository.Filter(x => x.CreatedBy == _currentUser.UserId);
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<ExpenseDto?> GetExpenseByIdAsync(long id)
    {
        try
        {
            var expense = (await _expenseRepository.Filter(x => x.Id == id && x.CreatedBy == _currentUser.UserId))
                .FirstOrDefault();
            return expense is null ? null : _mapper.Map<ExpenseDto>(expense);
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

			var expenses = await _expenseRepository.Filter(
				x => x.CreatedBy == _currentUser.UserId &&
				     (x.ExpenseDate ?? x.EntryDate) >= firstDayOfMonth &&
				     (x.ExpenseDate ?? x.EntryDate) <= lastDayOfMonth,
				"ExpenseCategory");

			var expensesGroup = expenses.GroupBy(x => new

			{
				x.ExpenseCategoryId,
				x.ExpenseCategory?.Name

			}).Select(g => new ExpenseCategoryGroupDto()
			{
				CategoryId = g.Key.ExpenseCategoryId ?? 0,
				CategoryName = g.Key.Name ?? "Uncategorized",
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

    public async Task<IEnumerable<ExpenseCategoryGroupDto>> GetExpensesOfAMonthAsync(int monthNo, int year)
    {
        if (monthNo < 1 || monthNo > 12)
        {
            throw new ArgumentOutOfRangeException(nameof(monthNo), "Month number must be between 1 and 12.");
        }

        var currentYear = DateTime.UtcNow.Year;
        if (year < 1 || year > currentYear + 1)
        {
            throw new ArgumentOutOfRangeException(nameof(year), $"Year must be between 1 and {currentYear + 1}.");
        }

        try
        {
            var firstDayOfMonth = new DateTime(year, monthNo, 1, 0, 0, 0, DateTimeKind.Utc);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            var expenses = await _expenseRepository.Filter(
                x => x.CreatedBy == _currentUser.UserId &&
                     (x.ExpenseDate ?? x.EntryDate) >= firstDayOfMonth &&
                     (x.ExpenseDate ?? x.EntryDate) <= lastDayOfMonth,
                "ExpenseCategory");

            var expensesGroup = expenses.GroupBy(x => new

            {
                x.ExpenseCategoryId,
                x.ExpenseCategory?.Name

            }).Select(g => new ExpenseCategoryGroupDto()
            {
                CategoryId = g.Key.ExpenseCategoryId ?? 0,
                CategoryName = g.Key.Name ?? "Uncategorized",
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

            var expenses = await _expenseRepository.Filter(
                x => x.CreatedBy == _currentUser.UserId &&
                     (x.ExpenseDate ?? x.EntryDate) >= firstDayOfMonth &&
                     (x.ExpenseDate ?? x.EntryDate) <= lastDayOfMonth,
                "ExpenseCategory");

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

            var expenses = await _expenseRepository.Filter(
                x => x.CreatedBy == _currentUser.UserId &&
                     (x.ExpenseDate ?? x.EntryDate) >= firstDayOfMonth &&
                     (x.ExpenseDate ?? x.EntryDate) <= lastDayOfMonth,
                "ExpenseCategory");

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
                                                                x.EntryDate <= lastDayOfMonth &&
                                                                x.CreatedBy == _currentUser.UserId);
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
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId &&
                                                                x.CreatedBy == _currentUser.UserId);
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
            // Server-owned: whatever the payload claimed, the row belongs to the caller.
            expense.CreatedBy = _currentUser.UserId;
            await _expenseRepository.InsertAsync(expense);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    
    
    
    public async Task<bool> UpdateExpenseAsync(long id, ExpenseDto expenseDto)
    {
        try
        {
            var expense = (await _expenseRepository.Filter(x => x.Id == id && x.CreatedBy == _currentUser.UserId))
                .FirstOrDefault();

            if (expense is null)
            {
                return false;
            }

            // Only fields the caller owns the value of are copied. CreatedBy and EntryDate stay
            // server-owned: letting them through would erase ownership (the row would disappear
            // from the owner's own lists) and reset the month-window fallback on every edit.
            expense.Cause = expenseDto.Cause;
            expense.Amount = expenseDto.Amount;
            expense.Quantity = expenseDto.Quantity;
            expense.UnitPrice = expenseDto.UnitPrice;
            expense.ExpenseDate = expenseDto.ExpenseDate;
            expense.ExpenseCategoryId = expenseDto.ExpenseCategoryId;
            expense.ReceiptId = expenseDto.ReceiptId;

            await _expenseRepository.UpdateAsync(expense);
            return true;
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
    
    public async Task<bool> DeleteExpenseAsync(long id)
    {
        try
        {
            var expense = (await _expenseRepository.Filter(x => x.Id == id && x.CreatedBy == _currentUser.UserId))
                .FirstOrDefault();

            if (expense is null)
            {
                return false;
            }

            await _expenseRepository.DeleteAsync(id);
            return true;
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
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId &&
                                                                x.CreatedBy == _currentUser.UserId);
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
            var expenses = await _expenseRepository.Filter(
                x => x.CreatedBy == _currentUser.UserId &&
                     x.ExpenseCategoryId == categoryId &&
                     (x.ExpenseDate ?? x.EntryDate) >= firstDayOfMonth &&
                     (x.ExpenseDate ?? x.EntryDate) <= lastDayOfMonth);
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
                                                                x.EntryDate == day &&
                                                                x.CreatedBy == _currentUser.UserId);
            return _mapper.Map<IEnumerable<ExpenseDto>>(expenses);
        }
        catch (Exception e)
        {
            _logger.LogError(e, e.Message);
            throw;
        }
    }
}
