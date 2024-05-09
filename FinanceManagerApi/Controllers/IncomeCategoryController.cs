using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class IncomeCategoryController : ControllerBase
    {
         private readonly IGenericRepository<IncomeCategory> _incomeCategoryRepository;

    public IncomeCategoryController(IGenericRepository<IncomeCategory> incomeCategoryRepository)
    {
        _incomeCategoryRepository = incomeCategoryRepository ?? throw new ArgumentNullException(nameof(incomeCategoryRepository));
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var incomeCategories = await _incomeCategoryRepository.GetAllAsync();
        return Ok(incomeCategories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        try
        {
            var incomeCategory = await _incomeCategoryRepository.GetByIdAsync(id);
            if (incomeCategory == null)
            {
                return NotFound();
            }
            return Ok(incomeCategory);
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            return BadRequest(e.Message);
        }
    }

    [HttpPost]
    public async Task<IActionResult> Create(IncomeCategoryDto incomeCategoryDto)
    {
        var incomeCategory = new IncomeCategory()
        {
            Name = incomeCategoryDto.Name,
            EntryDate = incomeCategoryDto.EntryDate,
            CreatedBy = Guid.Empty,
            Incomes = new List<Income>(),
        };
        await _incomeCategoryRepository.InsertAsync(incomeCategory);
        return CreatedAtAction(nameof(GetById), new { id = incomeCategory.Id }, incomeCategory);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(long id, IncomeCategory incomeCategory)
    {
        if (id != incomeCategory.Id)
        {
            return BadRequest();
        }

        try
        {
            await _incomeCategoryRepository.UpdateAsync(incomeCategory);
        }
        catch (Exception)
        {
            if (await _incomeCategoryRepository.GetByIdAsync(id) == null)
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
        var incomeCategory = await _incomeCategoryRepository.GetByIdAsync(id);

        await _incomeCategoryRepository.DeleteAsync(id);

        return NoContent();
    }
    }
}
