using System.Security.Claims;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Repository;
using FinanceManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ReceiptController : ControllerBase
{
    private readonly IReceiptService _receiptService;
    private readonly IGeminiReceiptScanService _geminiReceiptScanService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ReceiptController(
        IGenericRepository<Receipt> receiptRepository,
        IReceiptService receiptService,
        IGeminiReceiptScanService geminiReceiptScanService,
        IHttpContextAccessor httpContextAccessor)
    {
        _receiptService = receiptService;
        _geminiReceiptScanService = geminiReceiptScanService;
        _httpContextAccessor = httpContextAccessor;
    }

    [HttpPost("Scan")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> Scan([FromForm] IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest("A receipt file is required.");
        }

        if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only receipt images and PDF files are supported.");
        }

        await using var stream = file.OpenReadStream();
        var result = await _geminiReceiptScanService.ScanAsync(
            stream,
            file.ContentType,
            cancellationToken);
        return Ok(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var receiptCategories = await _receiptService.GetAllReceiptsAsync();
        return Ok(receiptCategories);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(long id)
    {
        var receipt = await _receiptService.GetReceiptByIdAsync(id);
        return Ok(receipt);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ReceiptDto receiptDto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }
        var userId = _httpContextAccessor.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }
        
        var createdBy = new Guid(userId);
        var entryDate = DateTime.UtcNow;

        receiptDto.CreatedBy = createdBy;
        receiptDto.EntryDate = entryDate;

        receiptDto.Expenses.ForEach(expense =>
        {
            expense.CreatedBy = createdBy;
            expense.EntryDate = entryDate;
        });
        
        await _receiptService.CreateReceiptAsync(receiptDto);
        return Ok();
    }

    // [HttpPut("{id}")]
    // public async Task<IActionResult> Update(long id, ExpenseDto receiptDto)
    // {
    //     if (id != expenseDto.Id)
    //     {
    //         return BadRequest();
    //     }
    //     try
    //     {
    //         await _expenseService.UpdateExpenseAsync(id, expenseDto);
    //     }
    //     catch (Exception)
    //     {
    //         if (await _expenseService.GetExpenseByIdAsync(id) == null)
    //         {
    //             return NotFound();
    //         }
    //         return BadRequest();
    //     }
    //
    //     return NoContent();
    // }
    //
    // [HttpDelete("{id}")]
    // public async Task<IActionResult> Delete(long id)
    // {
    //     var expense = await _expenseService.GetExpenseByIdAsync(id);
    //
    //     await _expenseService.DeleteExpenseAsync(id);
    //
    //     return NoContent();
    // }
}