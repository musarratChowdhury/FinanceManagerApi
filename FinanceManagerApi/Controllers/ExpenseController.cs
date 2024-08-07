﻿using System.Security.Claims;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using FinanceManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]/[action]")]
public class ExpenseController : ControllerBase
{
    private readonly IExpenseService _expenseService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ExpenseController(IGenericRepository<Expense> expenseRepository, IExpenseService expenseService, IHttpContextAccessor httpContextAccessor)
    {
        _expenseService = expenseService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var expenseCategories = await _expenseService.GetAllExpensesAsync();
        return Ok(expenseCategories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        var expense = await _expenseService.GetExpenseByIdAsync(id);
        return Ok(expense);
    }

    [HttpGet]
    public async Task<IActionResult> GetExpensesOfThisMonth()
    {
        try
        {
            var expenses = await _expenseService.GetExpensesOfThisMonthAsync();

            return Ok(expenses);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetExpensesOfAMonth(int month)
    {
        try
        {
            var expenses = await _expenseService.GetExpensesOfAMonthAsync(month);

            return Ok(expenses);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetBarChartData()
    {
        try
        {
            var barchartData = await _expenseService.GetMonthlyExpensesForThisYearAsync();
            return Ok(barchartData);
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
            var expenses = await _expenseService.GetExpensesOfADayAsync(day);
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
            var totalExpense = await _expenseService.GetTotalAmountExpensesOfACategoryAsync(categoryId);
            return Ok(totalExpense);
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
            var expenses = await _expenseService.GetExpensesOfACategoryAsync(categoryId);
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
        var userId = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        expenseDto.CreatedBy = new Guid(userId);
        expenseDto.EntryDate = DateTime.UtcNow;
        await _expenseService.CreateExpenseAsync(expenseDto);
        return Ok();
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, ExpenseDto expenseDto)
    {
        if (id != expenseDto.Id)
        {
            return BadRequest();
        }
        try
        {
            await _expenseService.UpdateExpenseAsync(id, expenseDto);
        }
        catch (Exception)
        {
            if (await _expenseService.GetExpenseByIdAsync(id) == null)
            {
                return NotFound();
            }
            return BadRequest();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(long id)
    {
        var expense = await _expenseService.GetExpenseByIdAsync(id);

        await _expenseService.DeleteExpenseAsync(id);

        return NoContent();
    }
}