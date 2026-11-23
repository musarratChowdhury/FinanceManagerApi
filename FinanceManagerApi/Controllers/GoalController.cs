using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

/// <summary>
/// Savings goals and their contribution ledger. Every route is scoped to the caller;
/// a goal owned by somebody else answers 404, never 403.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
public class GoalController : ControllerBase
{
    private readonly ISavingsGoalService _goalService;

    public GoalController(ISavingsGoalService goalService)
    {
        _goalService = goalService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _goalService.GetAllAsync());
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> GetById(long id)
    {
        var goal = await _goalService.GetByIdAsync(id);
        return goal is null ? NotFound(new { message = "Goal not found." }) : Ok(goal);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSavingsGoalDto? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("A goal name is required.");
        }

        if (!double.IsFinite(request.TargetAmount) || request.TargetAmount <= 0)
        {
            return BadRequest("A target amount greater than zero is required.");
        }

        if (request.InitialSaved is < 0)
        {
            return BadRequest("Amount already saved cannot be negative.");
        }

        var goal = await _goalService.CreateAsync(request);
        return CreatedAtAction(nameof(GetById), new { id = goal.Id }, goal);
    }

    [HttpPut("{id:long}")]
    public async Task<IActionResult> Update(long id, [FromBody] UpdateSavingsGoalDto? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest("A goal name is required.");
        }

        if (!double.IsFinite(request.TargetAmount) || request.TargetAmount <= 0)
        {
            return BadRequest("A target amount greater than zero is required.");
        }

        var goal = await _goalService.UpdateAsync(id, request);
        return goal is null ? NotFound(new { message = "Goal not found." }) : Ok(goal);
    }

    [HttpDelete("{id:long}")]
    public async Task<IActionResult> Delete(long id)
    {
        return await _goalService.DeleteAsync(id)
            ? NoContent()
            : NotFound(new { message = "Goal not found." });
    }

    /// <summary>"Add to saved" — one ledger row, not an overwritten total.</summary>
    [HttpPost("{id:long}/contribution")]
    public async Task<IActionResult> AddContribution(long id, [FromBody] AddGoalContributionDto? request)
    {
        if (request is null || !double.IsFinite(request.Amount) || request.Amount <= 0)
        {
            return BadRequest("A contribution amount greater than zero is required.");
        }

        var goal = await _goalService.AddContributionAsync(id, request);
        return goal is null ? NotFound(new { message = "Goal not found." }) : Ok(goal);
    }

    /// <summary>Removes a mis-entered contribution, which is the only supported correction.</summary>
    [HttpDelete("{id:long}/contribution/{contributionId:long}")]
    public async Task<IActionResult> DeleteContribution(long id, long contributionId)
    {
        return await _goalService.DeleteContributionAsync(id, contributionId)
            ? NoContent()
            : NotFound(new { message = "Contribution not found." });
    }
}
