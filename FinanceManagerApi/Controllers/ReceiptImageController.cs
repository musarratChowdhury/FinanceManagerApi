using FinanceManagerApi.Models.Errors;
using FinanceManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

/// <summary>
/// The softcopy of a receipt. One photo per receipt: POST replaces, so the
/// client can re-upload without creating duplicates.
/// </summary>
[ApiController]
[Authorize]
[Route("api/Receipt/{receiptId:long}/image")]
public class ReceiptImageController : ControllerBase
{
    /// <summary>
    /// Slightly above the configured 5 MB cap so an over-limit file is rejected
    /// with a clear 413 rather than by Kestrel before the action runs.
    /// </summary>
    private const long MaxRequestBytes = 6 * 1024 * 1024;

    private readonly IReceiptImageService _receiptImageService;

    public ReceiptImageController(IReceiptImageService receiptImageService)
    {
        _receiptImageService = receiptImageService;
    }

    /// <summary>Stores the photo, replacing any existing one for this receipt.</summary>
    [HttpPost]
    [RequestSizeLimit(MaxRequestBytes)]
    public async Task<IActionResult> Upload(
        long receiptId,
        [FromForm] IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { message = "A receipt photo is required." });
        }

        try
        {
            var image = await _receiptImageService.UploadAsync(receiptId, file, cancellationToken);
            return Ok(image);
        }
        catch (ReceiptImageException exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>Metadata for this receipt's photo, or 404 when it has none.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(long receiptId, CancellationToken cancellationToken)
    {
        try
        {
            var image = await _receiptImageService.GetAsync(receiptId, cancellationToken);
            return image is null ? NotFound() : Ok(image);
        }
        catch (ReceiptImageException exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>
    /// A short-lived download URL. The bucket stays private, so this is the only
    /// way a photo reaches the browser, and only after the ownership check.
    /// </summary>
    [HttpGet("url")]
    public async Task<IActionResult> GetUrl(
        long receiptId,
        [FromQuery] int? ttlSeconds,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = await _receiptImageService.GetUrlAsync(receiptId, ttlSeconds, cancellationToken);
            return url is null ? NotFound() : Ok(url);
        }
        catch (ReceiptImageException exception)
        {
            return Failure(exception);
        }
    }

    /// <summary>Removes the photo. The object goes first, then the row.</summary>
    [HttpDelete]
    public async Task<IActionResult> Delete(long receiptId, CancellationToken cancellationToken)
    {
        try
        {
            var removed = await _receiptImageService.DeleteAsync(receiptId, cancellationToken);
            return removed ? NoContent() : NotFound();
        }
        catch (ReceiptImageException exception)
        {
            return Failure(exception);
        }
    }

    private ObjectResult Failure(ReceiptImageException exception) =>
        StatusCode(exception.StatusCode, exception.Payload ?? new { message = exception.Message });
}
