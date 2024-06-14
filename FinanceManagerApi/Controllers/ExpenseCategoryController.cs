using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace FinanceManagerApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ExpenseCategoryController : ControllerBase
{
    private readonly IGenericRepository<ExpenseCategory> _expenseCategoryRepository;
	private readonly IHttpContextAccessor _httpContextAccessor;

	public ExpenseCategoryController(IGenericRepository<ExpenseCategory> expenseCategoryRepository, IHttpContextAccessor httpContextAccessor)
    {
        _expenseCategoryRepository = expenseCategoryRepository ?? throw new ArgumentNullException(nameof(expenseCategoryRepository));
        _httpContextAccessor = httpContextAccessor;
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
		var userId = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (string.IsNullOrEmpty(userId))
		{
			return Unauthorized();
		}
		var expenseCategory = new ExpenseCategory()
        {
            Name = expenseCategoryDto.Name,
            EntryDate = DateTime.UtcNow,
            CreatedBy = new Guid(userId),
            Expenses = new List<Expense>(),
        };
        await _expenseCategoryRepository.InsertAsync(expenseCategory);
        return CreatedAtAction(nameof(GetById), new { id = expenseCategory.Id }, expenseCategory);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, ExpenseCategory expenseCategory)
    {
		var userId = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
		if (string.IsNullOrEmpty(userId))
		{
			return Unauthorized();
		}
		if (id != expenseCategory.Id)
        {
            return BadRequest();
        }

        try
        {
			expenseCategory.CreatedBy = new Guid(userId);
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