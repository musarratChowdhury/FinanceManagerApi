using FinanceManagerApi.DbContext;
using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Models.Entity;
using Microsoft.EntityFrameworkCore;

namespace FinanceManagerApi.Services;

/// <summary>
/// Well-known setting keys. Keep the strings stable — they are persisted in the
/// database and read by the client, so a rename is a data migration.
/// </summary>
public static class SettingKeys
{
    /// <summary>Gemini model used for receipt scans. Value is a JSON string.</summary>
    public const string ReceiptScanModel = "ReceiptScanModel";
}

public interface ISettingsService
{
    /// <summary>All settings, ordered by key.</summary>
    Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>One setting's raw value, or null when it has never been set.</summary>
    Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Insert or update one setting.</summary>
    Task<SettingDto> SetValueAsync(
        string key,
        string value,
        string? description = null,
        CancellationToken cancellationToken = default);

    /// <summary>Insert or update many settings in a single transaction.</summary>
    Task<IReadOnlyList<SettingDto>> SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a setting. Returns false when it was not set.</summary>
    Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class SettingsService : ISettingsService
{
    private readonly FinanceDbContext _context;
    private readonly ILogger<SettingsService> _logger;

    public SettingsService(FinanceDbContext context, ILogger<SettingsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Settings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .Select(s => new SettingDto
            {
                Key = s.Key,
                Value = s.Value,
                Description = s.Description
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _context.Settings
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        return setting?.Value;
    }

    public async Task<SettingDto> SetValueAsync(
        string key,
        string value,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var setting = await UpsertAsync(key, value, description, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
        return ToDto(setting);
    }

    public async Task<IReadOnlyList<SettingDto>> SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SettingDto>(values.Count);

        foreach (var (key, value) in values)
        {
            var setting = await UpsertAsync(key, value, null, cancellationToken);
            results.Add(ToDto(setting));
        }

        await _context.SaveChangesAsync(cancellationToken);
        return results;
    }

    public async Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var setting = await _context.Settings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is null)
        {
            return false;
        }

        _context.Settings.Remove(setting);
        await _context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Setting {Key} deleted", key);
        return true;
    }

    private async Task<Settings> UpsertAsync(
        string key,
        string value,
        string? description,
        CancellationToken cancellationToken)
    {
        var setting = await _context.Settings
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (setting is null)
        {
            setting = new Settings
            {
                Key = key,
                Value = value,
                Description = description
            };
            _context.Settings.Add(setting);
            _logger.LogInformation("Setting {Key} created", key);
        }
        else
        {
            setting.Value = value;
            if (description is not null)
            {
                setting.Description = description;
            }
            _logger.LogInformation("Setting {Key} updated", key);
        }

        return setting;
    }

    private static SettingDto ToDto(Settings setting) => new()
    {
        Key = setting.Key,
        Value = setting.Value,
        Description = setting.Description
    };
}
