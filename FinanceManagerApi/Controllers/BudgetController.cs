using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

/// <summary>
/// Category ceilings for the Budgets screen.
///
/// One limit per (user, category) that carries forward every month; the spend it is
/// compared against is always the spend of the month being viewed.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class BudgetController : ControllerBase
{
    private readonly IBudgetService _budgetService;

    public BudgetController(IBudgetService budgetService)
    {
        _budgetService = budgetService;
    }

    /// <summary>
    /// Everything the screen needs for one month: limits joined with that month's
    /// spend, plus the projection and safe-to-spend figures.
    /// </summary>
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(int month, int? year = null)
    {
        var resolvedYear = year ?? DateTime.UtcNow.Year;

        try
        {
            return Ok(await _budgetService.GetMonthAsync(month, resolvedYear));
        }
        catch (ArgumentOutOfRangeException e)
        {
            return BadRequest(e.Message);
        }
    }

    /// <summary>Insert or update the caller's ceiling for one category.</summary>
    [HttpPost]
    public async Task<IActionResult> Save([FromBody] SaveBudgetLimitDto? request)
    {
        if (request is null || request.CategoryId <= 0)
        {
            return BadRequest("A category is required.");
        }

        if (!double.IsFinite(request.MonthlyLimit) || request.MonthlyLimit <= 0)
        {
            return BadRequest("A monthly limit greater than zero is required.");
        }

        var saved = await _budgetService.UpsertLimitAsync(request);

        return saved is null
            ? NotFound(new { message = $"Expense category {request.CategoryId} does not exist." })
            : Ok(saved);
    }

    /// <summary>Remove the ceiling, so the category goes back to "no budget".</summary>
    [HttpDelete("{categoryId:long}")]
    public async Task<IActionResult> Delete(long categoryId)
    {
        return await _budgetService.DeleteLimitAsync(categoryId)
            ? NoContent()
            : NotFound(new { message = "This category has no budget." });
    }
}
