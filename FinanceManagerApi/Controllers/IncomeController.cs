using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using FinanceManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    [Authorize]
    public class IncomeController : ControllerBase
    {
         private readonly IIncomeService _incomeService;

    public IncomeController(IGenericRepository<Income> incomeRepository, IIncomeService incomeService)
    {
        _incomeService = incomeService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var incomeCategories = await _incomeService.GetAllIncomesAsync();
        return Ok(incomeCategories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        // 404 also covers "exists but belongs to someone else", so the endpoint cannot be used
        // to probe which ids exist.
        var Income = await _incomeService.GetIncomeByIdAsync(id);
        if (Income is null)
        {
            return NotFound();
        }

        return Ok(Income);
    }

    [HttpGet]
    public async Task<IActionResult> GetIncomesOfThisMonth()
    {
        try
        {
            var incomes = await _incomeService.GetIncomesOfThisMonthAsync();

            return Ok(incomes);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetIncomesOfAMonth(int month, int? year = null)
    {
        try
        {
            var resolvedYear = year ?? DateTime.UtcNow.Year;
            var incomes = await _incomeService.GetIncomesOfAMonthAsync(month, resolvedYear);
            return Ok(incomes);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetIncomesOfADay(DateTime day)
    {
        try
        {
            var incomes = await _incomeService.GetIncomesOfADayAsync(day);
            return Ok(incomes);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
       
    }
    
    [HttpGet]
    public async Task<IActionResult> GetTotalAmountIncomesOfACategory(long categoryId)
    {
        try
        {
            var totalIncome = await _incomeService.GetTotalAmountIncomesOfACategoryAsync(categoryId);
            return Ok(totalIncome);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }
    
    [HttpGet]
    public async Task<IActionResult> GetIncomesOfACategory(long categoryId)
    {
        try
        {
            var incomes = await _incomeService.GetIncomesOfACategoryAsync(categoryId);
            return Ok(incomes);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(IncomeDto incomeDto)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        incomeDto.CreatedBy = new Guid(userId);
        await _incomeService.CreateIncomeAsync(incomeDto);
        return Ok();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, IncomeDto incomeDto)
    {
        if (id != incomeDto.Id)
        {
            return BadRequest();
        }
        try
        {
            var updated = await _incomeService.UpdateIncomeAsync(id, incomeDto);
            if (!updated)
            {
                return NotFound();
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var deleted = await _incomeService.DeleteIncomeAsync(id);
        if (!deleted)
        {
            return NotFound();
        }

        return NoContent();
    }
    }
}
