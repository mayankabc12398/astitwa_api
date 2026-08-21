using System.Text.Json;
using HrSuite.Core.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace HrSuite.Configuration;

/// <summary>
/// Layer 2. Turns cfg_setting and cfg_field_rule rows into the values base code asks for.
///
/// The cache is per tenant and short-lived, so an implementation engineer can change a row
/// and see the effect without a build, a deploy or a restart. Nothing here knows a client name;
/// it only knows keys.
/// </summary>
public sealed class ConfigResolver : IConfigResolver
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    private readonly ConfigRepository _repository;
    private readonly IMemoryCache _cache;
    private readonly ITenantContext _tenant;
    private readonly ILogger<ConfigResolver> _log;

    public ConfigResolver(
        ConfigRepository repository,
        IMemoryCache cache,
        ITenantContext tenant,
        ILogger<ConfigResolver> log)
    {
        _repository = repository;
        _cache = cache;
        _tenant = tenant;
        _log = log;
    }

    // -----------------------------------------------------------------
    // Settings
    // -----------------------------------------------------------------

    public async Task<IReadOnlyDictionary<string, ConfigSetting>> GetAllSettingsAsync(CancellationToken ct = default)
    {
        var key = SettingsCacheKey(_tenant.TenantId);

        if (_cache.TryGetValue(key, out IReadOnlyDictionary<string, ConfigSetting>? cached) && cached is not null)
            return cached;

        var rows = await _repository.GetSettingsAsync(ct).ConfigureAwait(false);

        var map = rows.ToDictionary(
            r => r.SettingKey,
            r => new ConfigSetting(r.SettingKey, r.SettingValue, r.DataType),
            StringComparer.OrdinalIgnoreCase);

        var result = (IReadOnlyDictionary<string, ConfigSetting>)map;
        _cache.Set(key, result, CacheFor);
        return result;
    }

    public async Task<string?> GetStringAsync(string settingKey, CancellationToken ct = default)
    {
        var all = await GetAllSettingsAsync(ct).ConfigureAwait(false);
        return all.TryGetValue(settingKey, out var setting) ? setting.Value : null;
    }

    public async Task<int?> GetIntAsync(string settingKey, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(settingKey, ct).ConfigureAwait(false);
        return int.TryParse(raw, out var value) ? value : null;
    }

    public async Task<bool> GetBoolAsync(string settingKey, bool fallback = false, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(settingKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return fallback;

        return raw.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "y" or "on" => true,
            "0" or "false" or "no" or "n" or "off" => false,
            _ => fallback
        };
    }

    public async Task<T?> GetJsonAsync<T>(string settingKey, CancellationToken ct = default) where T : class
    {
        var raw = await GetStringAsync(settingKey, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        try
        {
            return JsonSerializer.Deserialize<T>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            // A malformed value is a configuration mistake, not a server fault. Fall back to null.
            _log.LogWarning(ex, "Setting {Key} for tenant {TenantId} is not valid JSON.", settingKey, _tenant.TenantId);
            return null;
        }
    }

    // -----------------------------------------------------------------
    // Field rules
    // -----------------------------------------------------------------

    public async Task<IReadOnlyList<FieldRule>> GetAllFieldRulesAsync(CancellationToken ct = default)
    {
        var key = RulesCacheKey(_tenant.TenantId);

        if (_cache.TryGetValue(key, out IReadOnlyList<FieldRule>? cached) && cached is not null) return cached;

        var rows = await _repository.GetFieldRulesAsync(ct).ConfigureAwait(false);
        var rules = rows.Select(r => r.ToContract()).ToList();

        _cache.Set(key, (IReadOnlyList<FieldRule>)rules, CacheFor);
        return rules;
    }

    public async Task<IReadOnlyList<FieldRule>> GetFieldRulesAsync(string screenKey, CancellationToken ct = default)
    {
        var all = await GetAllFieldRulesAsync(ct).ConfigureAwait(false);
        return all.Where(r => string.Equals(r.ScreenKey, screenKey, StringComparison.OrdinalIgnoreCase))
                  .OrderBy(r => r.SeqNo)
                  .ThenBy(r => r.FieldKey, StringComparer.OrdinalIgnoreCase)
                  .ToList();
    }

    // -----------------------------------------------------------------

    public void InvalidateTenantCache(int tenantId)
    {
        _cache.Remove(SettingsCacheKey(tenantId));
        _cache.Remove(RulesCacheKey(tenantId));
    }

    public Task SaveSettingAsync(string key, string? value, string dataType, CancellationToken ct)
    {
        InvalidateTenantCache(_tenant.TenantId);
        return _repository.SaveSettingAsync(key, value, dataType, ct);
    }

    public Task SaveFieldRuleAsync(FieldRule rule, CancellationToken ct)
    {
        InvalidateTenantCache(_tenant.TenantId);
        return _repository.SaveFieldRuleAsync(rule, ct);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string SettingsCacheKey(int tenantId) => $"cfg-settings:{tenantId}";
    private static string RulesCacheKey(int tenantId) => $"cfg-rules:{tenantId}";
}
