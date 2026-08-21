using HrSuite.Core.Abstractions;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Configuration;

/// <summary>
/// Layer 2 reads. Derives from the tenant-filtered repository base like everything else,
/// so a configuration read for one tenant cannot return another tenant's rows.
/// </summary>
public sealed class ConfigRepository : RepositoryBase
{
    public ConfigRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<IReadOnlyList<SettingRow>> GetSettingsAsync(CancellationToken ct)
        => QueryAsync<SettingRow>("sp_cfg_setting_list", ct: ct);

    public Task<IReadOnlyList<FieldRuleRow>> GetFieldRulesAsync(CancellationToken ct)
        => QueryAsync<FieldRuleRow>("sp_cfg_field_rule_list", ct: ct);

    public Task<IReadOnlyList<FieldRuleRow>> GetFieldRulesAsync(string screenKey, CancellationToken ct)
        => QueryAsync<FieldRuleRow>(
            "sp_cfg_field_rule_list_by_screen",
            ProcArgs.New().Set("screen_key", screenKey),
            ct);

    public Task<SettingRow?> SaveSettingAsync(string key, string? value, string dataType, CancellationToken ct)
        => ExecuteReturningAsync<SettingRow>(
            "sp_cfg_setting_save",
            ProcArgs.New()
                .Set("setting_key", key)
                .Set("setting_value", value)
                .Set("data_type", dataType),
            ct);

    public Task<FieldRuleRow?> SaveFieldRuleAsync(FieldRule rule, CancellationToken ct)
        => ExecuteReturningAsync<FieldRuleRow>(
            "sp_cfg_field_rule_save",
            ProcArgs.New()
                .Set("screen_key", rule.ScreenKey)
                .Set("field_key", rule.FieldKey)
                .Set("is_visible", rule.IsVisible)
                .Set("is_required", rule.IsRequired)
                .Set("label", rule.Label)
                .Set("seq_no", rule.SeqNo),
            ct);

    public sealed class SettingRow
    {
        public string SettingKey { get; set; } = string.Empty;
        public string? SettingValue { get; set; }
        public string DataType { get; set; } = "string";
    }

    public sealed class FieldRuleRow
    {
        public string ScreenKey { get; set; } = string.Empty;
        public string FieldKey { get; set; } = string.Empty;
        public bool IsVisible { get; set; } = true;
        public bool IsRequired { get; set; }
        public string? Label { get; set; }
        public int SeqNo { get; set; } = 10;

        public FieldRule ToContract() => new(ScreenKey, FieldKey, IsVisible, IsRequired, Label, SeqNo);
    }
}
