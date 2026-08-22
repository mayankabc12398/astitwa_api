using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class CustomFieldRepository : RepositoryBase, ICustomFieldRepository
{
    /// <summary>camelCase, matching the JSON_EXTRACT paths the procedures are written against.</summary>
    private static readonly JsonSerializerOptions ChildPayload = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// The product's own bounded lookups, by the key a field stores.
    ///
    /// An allowlist rather than a name the caller supplies: a field that could name a
    /// procedure would be a field that could name any procedure. Every entry here is
    /// declared in db/03_procs_hr.sql, which the architecture tests check.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> LookupProcedures =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["department"] = "sp_hr_department_lookup",
            ["designation"] = "sp_hr_designation_lookup",
            ["employee"] = "sp_hr_employee_lookup",
            ["leaveType"] = "sp_hr_leave_type_lookup"
        };

    public CustomFieldRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public async Task<IReadOnlyList<CustomField>> ListAsync(string? screenKey, CancellationToken ct = default)
    {
        var (fields, options, bindings) = await QueryThreeAsync<CustomField, CustomFieldOption, CustomFieldBinding>(
            "sp_cfg_custom_field_list_by_screen",
            ProcArgs.New().Set("screen_key", string.IsNullOrWhiteSpace(screenKey) ? null : screenKey),
            ct).ConfigureAwait(false);

        return Attach(fields, options, bindings);
    }

    public async Task<CustomField?> GetAsync(int fieldId, CancellationToken ct = default)
    {
        var (fields, options, bindings) = await QueryThreeAsync<CustomField, CustomFieldOption, CustomFieldBinding>(
            "sp_cfg_custom_field_get",
            ProcArgs.New().Set("field_id", fieldId),
            ct).ConfigureAwait(false);

        return Attach(fields, options, bindings).FirstOrDefault();
    }

    public Task<CustomField?> SaveAsync(CustomField field, CancellationToken ct = default)
        => ExecuteReturningAsync<CustomField>(
            "sp_cfg_custom_field_save",
            ProcArgs.New()
                .Set("field_id", field.FieldId)
                .Set("screen_key", field.ScreenKey)
                .Set("field_key", field.FieldKey)
                .Set("label", field.Label)
                .Set("control_type", field.ControlType)
                .Set("is_required", field.IsRequired)
                .Set("default_value", field.DefaultValue)
                .Set("range_min", field.RangeMin)
                .Set("range_max", field.RangeMax)
                .Set("max_length", field.MaxLength)
                .Set("regex_pattern", field.RegexPattern)
                .Set("help_text", field.HelpText)
                .Set("placeholder", field.Placeholder)
                .Set("section_key", field.SectionKey)
                .Set("seq_no", field.SeqNo)
                .Set("width", field.Width)
                .Set("data_source_type", field.DataSourceType)
                .Set("lookup_key", field.LookupKey)
                .Set("parent_field_key", field.ParentFieldKey)
                .Set("show_in_form", field.ShowInForm)
                .Set("show_in_detail", field.ShowInDetail)
                .Set("show_in_print", field.ShowInPrint)
                .Set("value_mode", field.ValueMode)
                .Set("formula_text", field.FormulaText)
                .Set("formula_refs_csv", field.FormulaRefsCsv)
                .Set("round_to", field.RoundTo)
                .Set("recalc_mode", field.RecalcMode)
                // Null leaves the option list alone; an empty array clears it. That
                // distinction is what lets a dropdown become a plain text control.
                .Set("options_json", OptionPayload(field)),
            ct);

    public Task DeleteAsync(int fieldId, CancellationToken ct = default)
        => ExecuteAsync("sp_cfg_custom_field_delete", ProcArgs.New().Set("field_id", fieldId), ct);

    public Task ReorderAsync(IReadOnlyList<CustomFieldOrderEntry> items, CancellationToken ct = default)
        => ExecuteAsync(
            "sp_cfg_custom_field_reorder",
            ProcArgs.New().Set("items_json", JsonSerializer.Serialize(items, ChildPayload)),
            ct);

    public async Task<CustomFieldUsage> UsageAsync(int fieldId, CancellationToken ct = default)
        => await QuerySingleAsync<CustomFieldUsage>(
               "sp_cfg_custom_field_usage",
               ProcArgs.New().Set("field_id", fieldId),
               ct).ConfigureAwait(false)
           ?? new CustomFieldUsage();

    public Task<IReadOnlyList<CustomValue>> ValuesAsync(string screenKey, int recordId, CancellationToken ct = default)
        => QueryAsync<CustomValue>(
            "sp_cfg_custom_value_list",
            ProcArgs.New().Set("screen_key", screenKey).Set("record_id", recordId),
            ct);

    public Task SaveValuesAsync(
        string screenKey, int recordId, IReadOnlyList<CustomValueEntry> values, CancellationToken ct = default)
        => ExecuteAsync(
            "sp_cfg_custom_value_save",
            ProcArgs.New()
                .Set("screen_key", screenKey)
                .Set("record_id", recordId)
                .Set("values_json", JsonSerializer.Serialize(values, ChildPayload)),
            ct);

    // -----------------------------------------------------------------
    // Bound dropdowns
    // -----------------------------------------------------------------

    public Task<IReadOnlyList<DataSource>> DataSourcesAsync(CancellationToken ct = default)
        => QueryAsync<DataSource>("sp_cfg_data_source_list", ct: ct);

    public Task<DataSource?> DataSourceAsync(int sourceId, CancellationToken ct = default)
        => QuerySingleAsync<DataSource>(
            "sp_cfg_data_source_get",
            ProcArgs.New().Set("source_id", sourceId),
            ct);

    public Task SaveBindingAsync(int fieldId, CustomFieldBinding binding, CancellationToken ct = default)
        => ExecuteAsync(
            "sp_cfg_custom_field_binding_save",
            ProcArgs.New()
                .Set("field_id", fieldId)
                .Set("source_id", binding.SourceId)
                .Set("result_path", binding.ResultPath)
                .Set("value_field", binding.ValueField)
                .Set("label_field", binding.LabelField)
                .Set("label_template", binding.LabelTemplate)
                .Set("static_params_json", binding.StaticParamsJson)
                .Set("search_param_name", binding.SearchParamName)
                .Set("parent_field_key", binding.ParentFieldKey)
                .Set("parent_param_name", binding.ParentParamName)
                .Set("cache_seconds", binding.CacheSeconds),
            ct);

    public Task DeleteBindingAsync(int fieldId, CancellationToken ct = default)
        => ExecuteAsync("sp_cfg_custom_field_binding_delete", ProcArgs.New().Set("field_id", fieldId), ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(string lookupKey, CancellationToken ct = default)
        => LookupProcedures.TryGetValue(lookupKey ?? string.Empty, out var procedure)
            ? QueryAsync<LookupItem>(procedure, ct: ct)
            : Task.FromResult<IReadOnlyList<LookupItem>>(Array.Empty<LookupItem>());

    // -----------------------------------------------------------------
    // Computed fields
    // -----------------------------------------------------------------

    public Task<IReadOnlyList<CustomField>> ComputedAsync(string screenKey, CancellationToken ct = default)
        => QueryAsync<CustomField>(
            "sp_cfg_custom_field_computed_list",
            ProcArgs.New().Set("screen_key", screenKey),
            ct);

    // -----------------------------------------------------------------
    // Audit and archive
    // -----------------------------------------------------------------

    public Task<PagedResult<CustomFieldAudit>> AuditAsync(PageRequest page, string? screenKey, CancellationToken ct = default)
        => QueryPagedAsync<CustomFieldAudit>(
            "sp_cfg_custom_field_audit_list",
            page,
            ProcArgs.New().Set("screen_key", string.IsNullOrWhiteSpace(screenKey) ? null : screenKey),
            ct);

    public Task AddAuditAsync(
        string? screenKey, int fieldId, string? fieldKey, string action,
        string? beforeJson, string? afterJson, bool success, string? errorText,
        CancellationToken ct = default)
        => ExecuteAsync(
            "sp_cfg_custom_field_audit_add",
            ProcArgs.New()
                .Set("screen_key", screenKey)
                .Set("field_id", fieldId)
                .Set("field_key", fieldKey)
                .Set("action", action)
                .Set("before_json", beforeJson)
                .Set("after_json", afterJson)
                .Set("success", success)
                .Set("error_text", errorText),
            ct);

    public async Task<int> ArchiveValuesAsync(int fieldId, CancellationToken ct = default)
        => await ScalarAsync<int>(
            "sp_cfg_custom_value_archive_add",
            ProcArgs.New().Set("field_id", fieldId),
            ct).ConfigureAwait(false);

    public Task<PagedResult<CustomValueArchiveRow>> ArchiveAsync(PageRequest page, int? fieldId, CancellationToken ct = default)
        => QueryPagedAsync<CustomValueArchiveRow>(
            "sp_cfg_custom_value_archive_list",
            page,
            ProcArgs.New().Set("field_id", fieldId),
            ct);

    // -----------------------------------------------------------------

    /// <summary>
    /// Most fields have neither options nor a binding, so both arrive as flat result sets
    /// and are matched up here rather than fetched per field.
    /// </summary>
    private static IReadOnlyList<CustomField> Attach(
        IReadOnlyList<CustomField> fields,
        IReadOnlyList<CustomFieldOption> options,
        IReadOnlyList<CustomFieldBinding> bindings)
    {
        var optionsByField = options.GroupBy(o => o.FieldId)
                                    .ToDictionary(g => g.Key, g => g.OrderBy(o => o.SeqNo).ToList());

        var bindingByField = bindings.GroupBy(b => b.FieldId)
                                     .ToDictionary(g => g.Key, g => g.First());

        foreach (var field in fields)
        {
            field.Options = optionsByField.TryGetValue(field.FieldId, out var own) ? own : new List<CustomFieldOption>();
            field.Binding = bindingByField.TryGetValue(field.FieldId, out var binding) ? binding : null;
        }

        return fields;
    }

    /// <summary>
    /// Only a field holding a typed-in list sends one. Anything else sends an empty array,
    /// so switching a dropdown to a text box or to a bound source actually clears the
    /// choices that were behind it.
    /// </summary>
    private static string OptionPayload(CustomField field)
        => JsonSerializer.Serialize(
            string.Equals(field.DataSourceType, "Static", StringComparison.OrdinalIgnoreCase)
                ? field.Options
                : new List<CustomFieldOption>(),
            ChildPayload);
}
