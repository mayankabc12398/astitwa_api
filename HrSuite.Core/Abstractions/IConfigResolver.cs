namespace HrSuite.Core.Abstractions;

/// <summary>Layer 2 contract. Layer 1 depends on this interface, never on a config row.</summary>
public interface IConfigResolver
{
    Task<string?> GetStringAsync(string settingKey, CancellationToken ct = default);
    Task<int?> GetIntAsync(string settingKey, CancellationToken ct = default);
    Task<bool> GetBoolAsync(string settingKey, bool fallback = false, CancellationToken ct = default);
    Task<T?> GetJsonAsync<T>(string settingKey, CancellationToken ct = default) where T : class;

    Task<IReadOnlyDictionary<string, ConfigSetting>> GetAllSettingsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FieldRule>> GetFieldRulesAsync(string screenKey, CancellationToken ct = default);
    Task<IReadOnlyList<FieldRule>> GetAllFieldRulesAsync(CancellationToken ct = default);
    void InvalidateTenantCache(int tenantId);
}

public sealed record ConfigSetting(string Key, string? Value, string DataType);

/// <summary>Per-tenant field behaviour. Drives <c>DynamicField</c> on the client.</summary>
public sealed record FieldRule(
    string ScreenKey,
    string FieldKey,
    bool IsVisible,
    bool IsRequired,
    string? Label,
    int SeqNo);
