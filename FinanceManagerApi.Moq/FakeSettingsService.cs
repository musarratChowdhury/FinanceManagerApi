using FinanceManagerApi.Models.DTO;
using FinanceManagerApi.Services;

namespace FinanceManagerApi.Moq;

/// <summary>
/// In-memory <see cref="ISettingsService"/> for tests that build
/// <see cref="GeminiReceiptScanService"/> without a database.
/// </summary>
internal sealed class FakeSettingsService : ISettingsService
{
    private readonly Dictionary<string, SettingDto> _settings = new(StringComparer.OrdinalIgnoreCase);

    public FakeSettingsService(IEnumerable<KeyValuePair<string, string>>? initial = null)
    {
        foreach (var (key, value) in initial ?? [])
        {
            _settings[key] = new SettingDto { Key = key, Value = value };
        }
    }

    public Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SettingDto> all = _settings.Values.OrderBy(s => s.Key).ToList();
        return Task.FromResult(all);
    }

    public Task<string?> GetValueAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings.TryGetValue(key, out var setting) ? setting.Value : null);
    }

    public Task<SettingDto> SetValueAsync(
        string key,
        string value,
        string? description = null,
        CancellationToken cancellationToken = default)
    {
        var dto = new SettingDto { Key = key, Value = value, Description = description };
        _settings[key] = dto;
        return Task.FromResult(dto);
    }

    public Task<IReadOnlyList<SettingDto>> SetManyAsync(
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken = default)
    {
        var results = values
            .Select(pair => new SettingDto { Key = pair.Key, Value = pair.Value })
            .ToList();

        foreach (var dto in results)
        {
            _settings[dto.Key] = dto;
        }

        return Task.FromResult<IReadOnlyList<SettingDto>>(results);
    }

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_settings.Remove(key));
    }
}
