using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

[ApiController]
[Route("[controller]")]
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
    public async Task<IActionResult> GetById(int id)
    {
        var expenseCategory = await _expenseCategoryRepository.GetByIdAsync(id);
        if (expenseCategory == null)
        {
            return NotFound();
        }
        return Ok(expenseCategory);
    }

    [HttpPost]
    public async Task<IActionResult> Create(ExpenseCategory expenseCategory)
    {
        await _expenseCategoryRepository.InsertAsync(expenseCategory);
        return CreatedAtAction(nameof(GetById), new { id = expenseCategory.Id }, expenseCategory);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, ExpenseCategory expenseCategory)
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
    public async Task<IActionResult> Delete(int id)
    {
        var expenseCategory = await _expenseCategoryRepository.GetByIdAsync(id);
        if (expenseCategory == null)
        {
            return NotFound();
        }

        await _expenseCategoryRepository.DeleteAsync(id);

        return NoContent();
    }
}