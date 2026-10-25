using System.Security.Claims;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using FinanceManagerApi.Models.Errors;
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
    public async Task<IActionResult> Scan(
        [FromForm] IFormFile? file,
        [FromForm] string? model,
        [FromForm] bool? strict,
        CancellationToken cancellationToken)
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

        if (!string.IsNullOrWhiteSpace(model) &&
            !GeminiReceiptScanService.TryNormalizeModelName(model, out _))
        {
            return BadRequest("That model name is not valid.");
        }

        await using var stream = file.OpenReadStream();

        try
        {
            var result = await _geminiReceiptScanService.ScanAsync(
                stream,
                file.ContentType,
                model,
                strict ?? false,
                cancellationToken);
            return Ok(result);
        }
        catch (GeminiScanException exception)
        {
            // 502 rather than the generic 500 from ExceptionMiddleware: the per-model
            // attempts (including Gemini's raw error body) are the useful part.
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new GeminiFailureDto { Message = exception.Message, Attempts = exception.Attempts });
        }
    }

    /// <summary>
    /// Models this API key can see, plus whether each one can generate content.
    /// The settings screen needs the raw list to explain why a model is unusable.
    /// </summary>
    [HttpGet("Models")]
    public async Task<IActionResult> GetModels(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _geminiReceiptScanService.ListModelsAsync(cancellationToken));
        }
        catch (GeminiScanException exception)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                new GeminiFailureDto { Message = exception.Message, Attempts = exception.Attempts });
        }
    }

    /// <summary>
    /// Availability sweep. Probes each candidate with the sample receipt and
    /// returns the first model that actually answered.
    /// </summary>
    [HttpPost("Probe")]
    public async Task<IActionResult> Probe(
        [FromBody] GeminiProbeRequest? request,
        CancellationToken cancellationToken)
    {
        var models = request?.Models;

        if (models is { Count: > 0 } &&
            models.Any(candidate => !GeminiReceiptScanService.TryNormalizeModelName(candidate, out _)))
        {
            return BadRequest("One of the model names is not valid.");
        }

        return Ok(await _geminiReceiptScanService.ProbeAsync(
            models,
            request?.Fresh ?? false,
            cancellationToken));
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