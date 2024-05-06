using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

[ApiController]
[Route("[controller]/[action]")]
public class ExpenseController : ControllerBase
{
    private readonly IGenericRepository<Expense> _expenseRepository;

    public ExpenseController(IGenericRepository<Expense> expenseRepository)
    {
        _expenseRepository = expenseRepository ?? throw new ArgumentNullException(nameof(expenseRepository));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var expenseCategories = await _expenseRepository.GetAllAsync();
        return Ok(expenseCategories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);
        return Ok(expense);
    }

    [HttpGet]
    public async Task<IActionResult> GetExpensesOfThisMonth()
    {
        try
        {
            var firstDayOfMonth = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);
            var expenses = await _expenseRepository.Filter(x => x.EntryDate >= firstDayOfMonth &&
                                                                x.EntryDate <= lastDayOfMonth);

            return Ok(expenses);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetExpensesOfADay(DateTime day)
    {
        try
        {
            var expenses = await _expenseRepository.Filter(x => x.EntryDate == day);
            return Ok(expenses);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
       
    }
    
    [HttpGet]
    public async Task<IActionResult> GetTotalAmountExpensesOfACategory(long categoryId)
    {
        try
        {
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId);
            return Ok(expenses.Sum(x => x.Amount));
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }
    
    [HttpGet]
    public async Task<IActionResult> GetExpensesOfACategory(long categoryId)
    {
        try
        {
            var expenses = await _expenseRepository.Filter(x => x.ExpenseCategoryId == categoryId);
            return Ok(expenses);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(ExpenseDto expenseDto)
    {
        var expense = new Expense()
        {
            Cause = expenseDto.Cause,
            Amount = expenseDto.Amount,
            EntryDate = expenseDto.EntryDate,
            CreatedBy = Guid.Empty,
            ExpenseCategoryId = expenseDto.ExpenseCategoryId
        };
        await _expenseRepository.InsertAsync(expense);
        return CreatedAtAction(nameof(GetById), new { id = expense.Id }, expense);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, Expense expense)
    {
        if (id != expense.Id)
        {
            return BadRequest();
        }

        try
        {
            await _expenseRepository.UpdateAsync(expense);
        }
        catch (Exception)
        {
            if (await _expenseRepository.GetByIdAsync(id) == null)
            {
                return NotFound();
            }
            throw;
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var expense = await _expenseRepository.GetByIdAsync(id);

        await _expenseRepository.DeleteAsync(id);

        return NoContent();
    }
}