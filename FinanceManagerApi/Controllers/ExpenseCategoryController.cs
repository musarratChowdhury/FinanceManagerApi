using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ExpenseCategoryController : ControllerBase
{
    private readonly IGenericRepository<ExpenseCategory> _expenseCategoryRepository;

    public ExpenseCategoryController(IGenericRepository<ExpenseCategory> expenseCategoryRepository)
    {
        _expenseCategoryRepository = expenseCategoryRepository ?? throw new ArgumentNullException(nameof(expenseCategoryRepository));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var expenseCategories = await _expenseCategoryRepository.GetAllAsync();
        return Ok(expenseCategories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        try
        {
            var expenseCategory = await _expenseCategoryRepository.GetByIdAsync(id);
            if (expenseCategory == null)
            {
                return NotFound();
            }
            return Ok(expenseCategory);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(ExpenseCategoryDto expenseCategoryDto)
    {
        var expenseCategory = new ExpenseCategory()
        {
            Name = expenseCategoryDto.Name,
            EntryDate = expenseCategoryDto.EntryDate,
            CreatedBy = Guid.Empty,
            Expenses = new List<Expense>(),
        };
        await _expenseCategoryRepository.InsertAsync(expenseCategory);
        return CreatedAtAction(nameof(GetById), new { id = expenseCategory.Id }, expenseCategory);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, ExpenseCategory expenseCategory)
    {
        if (id != expenseCategory.Id)
        {
            return BadRequest();
        }

        try
        {
            await _expenseCategoryRepository.UpdateAsync(expenseCategory);
        }
        catch (Exception)
        {
            if (await _expenseCategoryRepository.GetByIdAsync(id) == null)
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
        var expenseCategory = await _expenseCategoryRepository.GetByIdAsync(id);

        await _expenseCategoryRepository.DeleteAsync(id);

        return NoContent();
    }
}