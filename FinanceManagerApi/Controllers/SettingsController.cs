using System.Text.Json;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FinanceManagerApi.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly ISettingsService _settingsService;

    public SettingsController(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    /// <summary>Every stored setting, ordered by key.</summary>
    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        return Ok(await _settingsService.GetAllAsync(cancellationToken));
    }

    /// <summary>One setting by key. 404 when it has never been set.</summary>
    [HttpGet("{key}")]
    public async Task<IActionResult> Get(string key, CancellationToken cancellationToken)
    {
        var all = await _settingsService.GetAllAsync(cancellationToken);
        var setting = all.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

        return setting is null
            ? NotFound(new { message = $"Setting '{key}' is not set." })
            : Ok(setting);
    }

    /// <summary>Insert or replace one setting.</summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> Put(
        string key,
        [FromBody] UpdateSettingDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("A setting value is required.");
        }

        if (!TryNormalize(key, request.Value, out var value, out var error))
        {
            return BadRequest(error);
        }

        return Ok(await _settingsService.SetValueAsync(
            key,
            value,
            request.Description,
            cancellationToken));
    }

    /// <summary>
    /// Removes a setting, so the configured default applies again.
    /// 404 when it was not set, which is a no-op for "use server default".
    /// </summary>
    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key, CancellationToken cancellationToken)
    {
        return await _settingsService.DeleteAsync(key, cancellationToken)
            ? NoContent()
            : NotFound(new { message = $"Setting '{key}' is not set." });
    }

    /// <summary>Insert or replace many settings in one call (the settings form save).</summary>
    [HttpPut]
    public async Task<IActionResult> PutMany(
        [FromBody] UpdateSettingsDto? request,
        CancellationToken cancellationToken)
    {
        if (request is null || request.Values.Count == 0)
        {
            return BadRequest("At least one setting is required.");
        }

        var normalized = new Dictionary<string, string>(request.Values.Count);

        foreach (var (key, value) in request.Values)
        {
            if (!TryNormalize(key, value, out var canonical, out var error))
            {
                return BadRequest(error);
            }

            normalized[key] = canonical;
        }

        return Ok(await _settingsService.SetManyAsync(normalized, cancellationToken));
    }

    /// <summary>
    /// Validates a value and returns it in canonical form (a JSON string).
    /// Accepts `gemini-3.8-flash` and `"gemini-3.8-flash"` so the client is not
    /// forced to pre-quote, but always stores the quoted form.
    /// </summary>
    private static bool TryNormalize(string key, string value, out string normalized, out string error)
    {
        normalized = value;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(key))
        {
            error = "A setting key is required.";
            return false;
        }

        if (string.Equals(key, SettingKeys.ReceiptScanModel, StringComparison.OrdinalIgnoreCase))
        {
            var model = ReadString(value);
            if (model is null || !GeminiReceiptScanService.TryNormalizeModelName(model, out var valid))
            {
                error = $"'{value}' is not a valid Gemini model name.";
                return false;
            }

            normalized = JsonSerializer.Serialize(valid);
        }

        return true;
    }

    /// <summary>Reads a value that may arrive bare or JSON-quoted.</summary>
    private static string? ReadString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith('"'))
        {
            try
            {
                return JsonSerializer.Deserialize<string>(trimmed);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        return trimmed;
    }
}
