using System.Text.Json;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;
using HrSuite.Infrastructure.Schema;

namespace HrSuite.Infrastructure.Repositories;

/// <summary>
/// Metadata through stored procedures, schema through <see cref="ISchemaExecutor"/>.
///
/// The two halves are here together because they have to succeed or fail as one thought: a
/// metadata row describing a column that was never created is a field the form draws and the
/// save cannot store, and a column with no row is invisible to everything. There is no
/// transaction spanning them — MySQL commits DDL implicitly — so the order is chosen instead:
/// create the column first, then write the row. A failure between the two leaves an unused
/// column, which is inert and shows up in the audit; the reverse would leave a broken screen.
/// </summary>
public sealed class FieldColumnRepository : RepositoryBase, IFieldColumnRepository
{
    private readonly ISchemaExecutor _schema;

    public FieldColumnRepository(IDbConnectionFactory factory, ITenantContext tenant, ISchemaExecutor schema)
        : base(factory, tenant)
        => _schema = schema;

    // -----------------------------------------------------------------
    // Metadata
    // -----------------------------------------------------------------

    public Task<IReadOnlyList<FieldColumnScreen>> ScreensAsync(CancellationToken ct = default)
        => QueryAsync<FieldColumnScreen>("sp_cfg_fb_screen_list", ct: ct);

    public Task<FieldColumnScreen?> ScreenAsync(string screenCode, CancellationToken ct = default)
        => QuerySingleAsync<FieldColumnScreen>(
            "sp_cfg_fb_screen_get",
            ProcArgs.New().Set("screen_code", screenCode),
            ct);

    public async Task<FieldColumnLayout?> LayoutAsync(string screenCode, CancellationToken ct = default)
    {
        var screen = await ScreenAsync(screenCode, ct).ConfigureAwait(false);
        if (screen is null) return null;

        var fields = await QueryAsync<FieldColumn>(
            "sp_cfg_fb_field_list",
            ProcArgs.New().Set("screen_id", screen.ScreenId),
            ct).ConfigureAwait(false);

        var options = await QueryAsync<FieldColumnOption>(
            "sp_cfg_fb_option_list",
            ProcArgs.New().Set("screen_id", screen.ScreenId),
            ct).ConfigureAwait(false);

        // A metadata row whose column has already gone — dropped by hand, or a failed delete
        // that got half way — must not reach the runtime form. It is listed in the audit
        // instead, where somebody can see what happened to it.
        var live = await _schema.ColumnsOfAsync(screen.BaseTable, ct).ConfigureAwait(false);

        var byField = options.GroupBy(o => o.FieldId).ToDictionary(g => g.Key, g => g.ToList());
        var kept = fields.Where(f => !f.IsCustom || live.Contains(f.ColumnName)).ToList();
        foreach (var field in kept)
        {
            field.Options = byField.TryGetValue(field.FieldId, out var list) ? list : new List<FieldColumnOption>();
        }

        return new FieldColumnLayout
        {
            Screen = screen,
            Steps = screen.Steps.ToList(),
            Fields = kept,
        };
    }

    public Task<FieldColumn?> FieldAsync(int fieldId, CancellationToken ct = default)
        => QuerySingleAsync<FieldColumn>(
            "sp_cfg_fb_field_get",
            ProcArgs.New().Set("field_id", fieldId),
            ct);

    public Task<FieldColumn?> SaveMetadataAsync(FieldColumn field, CancellationToken ct = default)
        => ExecuteReturningAsync<FieldColumn>(
            "sp_cfg_fb_field_save",
            ProcArgs.New()
                .Set("field_id", field.FieldId)
                .Set("screen_id", field.ScreenId)
                .Set("field_key", field.FieldKey)
                .Set("label", field.Label)
                .Set("column_name", field.ColumnName)
                .Set("control_type", field.ControlType)
                .Set("sql_type", field.SqlType)
                .Set("is_required", field.IsRequired ? 1 : 0)
                .Set("default_value", field.DefaultValue)
                .Set("range_min", field.RangeMin)
                .Set("range_max", field.RangeMax)
                .Set("max_length", field.MaxLength)
                .Set("regex_pattern", field.RegexPattern)
                .Set("help_text", field.HelpText)
                .Set("placeholder", field.Placeholder)
                .Set("step_index", field.StepIndex)
                .Set("sort_order", field.SortOrder)
                .Set("width", field.Width)
                .Set("data_source_type", field.DataSourceType)
                .Set("show_in_form", field.ShowInForm ? 1 : 0)
                .Set("show_in_detail", field.ShowInDetail ? 1 : 0)
                .Set("show_in_print", field.ShowInPrint ? 1 : 0)
                .Set("options", OptionsJson(field)),
            ct);

    public Task DeleteMetadataAsync(int fieldId, CancellationToken ct = default)
        => ExecuteAsync("sp_cfg_fb_field_delete", ProcArgs.New().Set("field_id", fieldId), ct);

    public Task ReorderAsync(IReadOnlyList<FieldColumnPosition> items, CancellationToken ct = default)
        => ExecuteAsync(
            "sp_cfg_fb_field_reorder",
            ProcArgs.New().Set("items", JsonSerializer.Serialize(
                items.Select(i => new { fieldId = i.FieldId, stepIndex = i.StepIndex, sortOrder = i.SortOrder }))),
            ct);

    public Task<IReadOnlyList<FieldColumnAudit>> AuditAsync(string? screenCode, CancellationToken ct = default)
        => QueryAsync<FieldColumnAudit>(
            "sp_cfg_fb_audit_list",
            ProcArgs.New().Set("screen_code", string.IsNullOrWhiteSpace(screenCode) ? null : screenCode).Set("take", 100),
            ct);

    // -----------------------------------------------------------------
    // Schema
    // -----------------------------------------------------------------

    public Task<IReadOnlySet<string>> LiveColumnsAsync(string table, CancellationToken ct = default)
        => _schema.ColumnsOfAsync(table, ct);

    public Task<string> AddColumnAsync(
        FieldColumnScreen screen, string column, string sqlType, string? afterColumn, CancellationToken ct = default)
        => RunAuditedAsync(
            screen, "ADD", column,
            ColumnDdl.BuildAddColumn(screen.BaseTable, column, sqlType, afterColumn),
            ct);

    public Task<string> ChangeColumnAsync(
        FieldColumnScreen screen, string fromColumn, string toColumn, string sqlType, CancellationToken ct = default)
        => RunAuditedAsync(
            screen, "CHANGE", toColumn,
            ColumnDdl.BuildChangeColumn(screen.BaseTable, fromColumn, toColumn, sqlType),
            ct);

    public Task<string> MoveColumnAsync(
        FieldColumnScreen screen, string column, string sqlType, string? afterColumn, CancellationToken ct = default)
        => RunAuditedAsync(
            screen, "MOVE", column,
            ColumnDdl.BuildMoveColumn(screen.BaseTable, column, sqlType, afterColumn),
            ct);

    /// <summary>
    /// Archives what the column holds, then drops it.
    ///
    /// The archive is written first and in its own call: if the drop then fails, the values
    /// are stored twice, which costs nothing. The other order costs the data.
    /// </summary>
    public async Task<string> DropColumnAsync(FieldColumnScreen screen, FieldColumn field, CancellationToken ct = default)
    {
        var values = await _schema
            .ReadColumnAsync(screen.BaseTable, screen.PkColumn, field.ColumnName, ct)
            .ConfigureAwait(false);

        if (values.Count > 0)
        {
            await ExecuteAsync(
                "sp_cfg_fb_archive_add",
                ProcArgs.New()
                    .Set("screen_id", screen.ScreenId)
                    .Set("field_id", field.FieldId)
                    .Set("column_name", field.ColumnName)
                    .Set("rows", JsonSerializer.Serialize(
                        values.Select(v => new { recordId = v.RecordId, value = v.Value }))),
                ct).ConfigureAwait(false);
        }

        return await RunAuditedAsync(
            screen, "DROP", field.ColumnName,
            ColumnDdl.BuildDropColumn(screen.BaseTable, field.ColumnName),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a statement and records it either way.
    ///
    /// The failed attempts are the ones worth keeping: a column that is missing with no record
    /// of the attempt is a mystery, and a column that is missing with the error beside it is a
    /// five-minute fix.
    /// </summary>
    private async Task<string> RunAuditedAsync(
        FieldColumnScreen screen, string action, string column, string statement, CancellationToken ct)
    {
        try
        {
            await _schema.RunAsync(statement, ct).ConfigureAwait(false);
            await AuditAsync(screen, action, column, statement, true, null, ct).ConfigureAwait(false);
            return statement;
        }
        catch (Exception cause)
        {
            await AuditAsync(screen, action, column, statement, false, cause.Message, ct).ConfigureAwait(false);
            throw;
        }
    }

    private Task AuditAsync(
        FieldColumnScreen screen, string action, string column, string statement,
        bool success, string? error, CancellationToken ct)
        => ExecuteAsync(
            "sp_cfg_fb_audit_add",
            ProcArgs.New()
                .Set("screen_id", screen.ScreenId)
                .Set("action", action)
                .Set("table_name", screen.BaseTable)
                .Set("column_name", column)
                .Set("sql_text", statement)
                .Set("success", success ? 1 : 0)
                .Set("error_text", error is null ? null : error.Length > 500 ? error[..500] : error),
            ct);

    /// <summary>Static options as the JSON array the save procedure reads, or null to leave them alone.</summary>
    private static string? OptionsJson(FieldColumn field)
    {
        if (!string.Equals(field.DataSourceType, "Static", StringComparison.OrdinalIgnoreCase)) return null;

        return JsonSerializer.Serialize(
            (field.Options ?? new List<FieldColumnOption>())
                .Where(o => !string.IsNullOrWhiteSpace(o.OptionValue))
                .Select(o => new { value = o.OptionValue.Trim(), label = (o.OptionLabel ?? o.OptionValue).Trim() }));
    }
}
