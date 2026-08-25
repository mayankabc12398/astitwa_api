using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

/// <summary>
/// The rules for adding a column to a live screen.
///
/// Everything this service refuses is something that would be irreversible if it went wrong:
/// a column name that could collide with a shipped one, a type change that would truncate,
/// a delete of a field the product ships. What it allows is deliberately narrow — add a
/// nullable column, rename or widen it, drop it after archiving — because that is the set a
/// screen can survive being wrong about.
///
/// Placement is arithmetic, not a number the caller sends: "after Key skills, in Role" is
/// resolved here into a sort order and the anchor column the ALTER positions against.
/// </summary>
public sealed class FieldColumnService : IFieldColumnService
{
    private readonly IFieldColumnRepository _repository;

    public FieldColumnService(IFieldColumnRepository repository) => _repository = repository;

    // The DDL rules live in Infrastructure, which Core cannot reference. These three mirrors
    // are the contract between them; the guard in ColumnDdl is what actually decides, and it
    // refuses anything these let through.
    //
    // Declared above Controls deliberately: static initialisers run in declaration order, and
    // Controls reads SqlTypes while it is being built.
    private static readonly IReadOnlySet<string> ColumnDdlControls =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "text", "textarea", "number", "decimal", "date", "datetime", "checkbox", "dropdown", "radio",
            "multiselect", "file",
        };

    private static readonly IReadOnlyDictionary<string, string> SqlTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = "VARCHAR(255)",
            ["textarea"] = "TEXT",
            ["number"] = "INT",
            ["decimal"] = "DECIMAL(18,4)",
            ["date"] = "DATE",
            ["datetime"] = "DATETIME",
            ["checkbox"] = "TINYINT(1)",
            ["dropdown"] = "VARCHAR(255)",
            ["radio"] = "VARCHAR(255)",
            ["multiselect"] = "TEXT",
            ["file"] = "VARCHAR(500)",
        };

    private static readonly IReadOnlyDictionary<string, string[]> SafeWidening =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = new[] { "textarea" },
            ["dropdown"] = new[] { "text", "textarea", "radio", "multiselect" },
            ["radio"] = new[] { "text", "textarea", "dropdown", "multiselect" },
            ["number"] = new[] { "decimal", "text", "textarea" },
            ["date"] = new[] { "datetime" },
            ["checkbox"] = new[] { "number", "text" },
            ["multiselect"] = new[] { "textarea" },
        };

    /// <summary>What the builder offers. Labels live here; the column each one produces lives in the DDL guard.</summary>
    private static readonly IReadOnlyList<FieldColumnControlType> Controls = new[]
    {
        Control("text", "Text"),
        Control("textarea", "Paragraph"),
        Control("number", "Number"),
        Control("decimal", "Decimal"),
        Control("date", "Date"),
        Control("datetime", "Date & time"),
        Control("checkbox", "Checkbox"),
        Control("dropdown", "Dropdown"),
        Control("radio", "Radio group"),
        Control("multiselect", "Multi-select"),
        Control("file", "File path"),
    };

    public IReadOnlyList<FieldColumnControlType> ControlTypes() => Controls;

    public Task<IReadOnlyList<FieldColumnScreen>> ScreensAsync(CancellationToken ct = default)
        => _repository.ScreensAsync(ct);

    public async Task<Result<FieldColumnLayout>> LayoutAsync(string screenCode, CancellationToken ct = default)
    {
        var layout = await _repository.LayoutAsync(screenCode, ct).ConfigureAwait(false);
        return layout is null
            ? Result<FieldColumnLayout>.NotFound($"Screen '{screenCode}' is not registered for field configuration.")
            : Result<FieldColumnLayout>.Success(layout);
    }

    public async Task<Result<FieldColumn>> SaveAsync(string screenCode, FieldColumn field, CancellationToken ct = default)
    {
        var screen = await _repository.ScreenAsync(screenCode, ct).ConfigureAwait(false);
        if (screen is null) return Result<FieldColumn>.NotFound($"Screen '{screenCode}' is not registered.");

        var layout = await _repository.LayoutAsync(screenCode, ct).ConfigureAwait(false);
        var existing = layout?.Fields ?? new List<FieldColumn>();
        var isNew = field.FieldId == 0;

        var validation = new Validator()
            .RequireText(field.Label, "A label is required.", "label")
            .Require(ColumnDdlControls.Contains(field.ControlType), "Choose a control type.", "controlType")
            .ToResult();
        if (validation.IsFailure) return Result<FieldColumn>.Fail(validation.Errors.ToArray());

        var sqlType = ResolveSqlType(field.ControlType, field.MaxLength);
        if (sqlType is null)
            return Result<FieldColumn>.Fail(Error.Validation($"'{field.ControlType}' has no column type.", "controlType"));

        // ---------- existing field ----------
        if (!isNew)
        {
            var current = existing.FirstOrDefault(f => f.FieldId == field.FieldId);
            if (current is null) return Result<FieldColumn>.NotFound("That field no longer exists.");
            if (!current.IsCustom)
                return Result<FieldColumn>.Invalid("This column ships with the product and cannot be edited here.");

            if (!IsSafeChange(current.ControlType, field.ControlType))
            {
                return Result<FieldColumn>.Fail(Error.Validation(
                    $"Changing {current.ControlType} to {field.ControlType} could truncate what is already stored. Add a new field instead.",
                    "controlType"));
            }

            // The column is only touched when the type actually moves; a label edit is a row update.
            if (!string.Equals(current.SqlType, sqlType, StringComparison.OrdinalIgnoreCase))
            {
                await _repository.ChangeColumnAsync(screen, current.ColumnName, current.ColumnName, sqlType, ct)
                    .ConfigureAwait(false);
            }

            field.ScreenId = screen.ScreenId;
            field.ColumnName = current.ColumnName;
            field.FieldKey = current.FieldKey;
            field.SqlType = sqlType;
            field.StepIndex = ClampStep(field.StepIndex, screen);
            field.SortOrder = current.SortOrder;

            var updated = await _repository.SaveMetadataAsync(field, ct).ConfigureAwait(false);
            return updated is null
                ? Result<FieldColumn>.NotFound("That field no longer exists.")
                : Result<FieldColumn>.Success(updated);
        }

        // ---------- new field ----------
        var column = string.IsNullOrWhiteSpace(field.ColumnName)
            ? SlugColumn(field.Label)
            : Normalise(field.ColumnName);

        var nameProblem = ValidateColumnName(column);
        if (nameProblem is not null) return Result<FieldColumn>.Fail(Error.Validation(nameProblem, "columnName"));

        var live = await _repository.LiveColumnsAsync(screen.BaseTable, ct).ConfigureAwait(false);
        if (live.Contains(column))
            return Result<FieldColumn>.Fail(Error.Validation($"'{column}' already exists on {screen.BaseTable}.", "columnName"));
        if (existing.Any(f => string.Equals(f.FieldKey, column, StringComparison.OrdinalIgnoreCase)))
            return Result<FieldColumn>.Fail(Error.Validation($"A field with the key '{column}' is already configured.", "columnName"));

        field.StepIndex = ClampStep(field.StepIndex, screen);

        // Where it goes: the field it follows decides both the sort order the form reads and
        // the column the ALTER positions against, so the screen and the table agree.
        var step = existing.Where(f => f.StepIndex == field.StepIndex).OrderBy(f => f.SortOrder).ToList();
        var anchor = field.AfterFieldId > 0
            ? step.FirstOrDefault(f => f.FieldId == field.AfterFieldId)
            : step.LastOrDefault();

        field.SortOrder = NextSortOrder(step, anchor);
        field.ScreenId = screen.ScreenId;
        field.FieldKey = column;
        field.ColumnName = column;
        field.SqlType = sqlType;

        // Column first, metadata second: an unused column is inert and visible in the audit,
        // whereas a row with no column is a field the form draws and the save cannot store.
        await _repository.AddColumnAsync(screen, column, sqlType, anchor?.ColumnName, ct).ConfigureAwait(false);

        var saved = await _repository.SaveMetadataAsync(field, ct).ConfigureAwait(false);
        return saved is null
            ? Result<FieldColumn>.Invalid("The column was created but its definition could not be saved.")
            : Result<FieldColumn>.Success(saved);
    }

    public async Task<Result> DeleteAsync(int fieldId, string confirmColumnName, CancellationToken ct = default)
    {
        var field = await _repository.FieldAsync(fieldId, ct).ConfigureAwait(false);
        if (field is null) return Result.Fail(ErrorCode.NotFound, "That field no longer exists.");
        if (!field.IsCustom) return Result.Invalid("This column ships with the product and cannot be dropped here.");

        // The caller has to name the column it means. A delete that only needs an id is one
        // stale browser tab away from dropping the wrong column.
        if (!string.Equals(field.ColumnName, confirmColumnName, StringComparison.OrdinalIgnoreCase))
            return Result.Invalid("The column name does not match the field being deleted.");

        var screen = await _repository.ScreenAsync(await ScreenCodeOf(field, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        if (screen is null) return Result.Fail(ErrorCode.NotFound, "That screen is no longer registered.");

        await _repository.DropColumnAsync(screen, field, ct).ConfigureAwait(false);
        await _repository.DeleteMetadataAsync(fieldId, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> ReorderAsync(IReadOnlyList<FieldColumnPosition> items, CancellationToken ct = default)
    {
        if (items.Count == 0) return Result.Success();
        await _repository.ReorderAsync(items, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<FieldColumnAudit>> AuditAsync(string? screenCode, CancellationToken ct = default)
        => _repository.AuditAsync(screenCode, ct);

    // -----------------------------------------------------------------
    // Placement and naming
    // -----------------------------------------------------------------

    /// <summary>Halfway to the next field, so an insertion consumes a gap instead of pushing the rest down.</summary>
    private static int NextSortOrder(IReadOnlyList<FieldColumn> step, FieldColumn? anchor)
    {
        if (step.Count == 0) return 10;
        if (anchor is null) return Math.Max(1, step[0].SortOrder - 1);

        var index = step.ToList().FindIndex(f => f.FieldId == anchor.FieldId);
        if (index < 0 || index == step.Count - 1) return anchor.SortOrder + 10;

        var middle = (anchor.SortOrder + step[index + 1].SortOrder) / 2;
        return middle > anchor.SortOrder ? middle : anchor.SortOrder + 1;
    }

    private static int ClampStep(int step, FieldColumnScreen screen)
    {
        var count = Math.Max(1, screen.Steps.Count);
        return step < 0 ? 0 : step >= count ? count - 1 : step;
    }

    private static async Task<string> ScreenCodeOf(FieldColumn field, CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        // sp_cfg_fb_field_get returns the screen's code alongside the field, so no second read.
        return field.ScreenCode ?? string.Empty;
    }

    private static string? ResolveSqlType(string controlType, int? maxLength)
    {
        if (!SqlTypes.TryGetValue(controlType, out var sqlType)) return null;
        return controlType.Equals("text", StringComparison.OrdinalIgnoreCase) && maxLength is > 0 and <= 4000
            ? $"VARCHAR({maxLength})"
            : sqlType;
    }

    private static bool IsSafeChange(string from, string to) =>
        string.Equals(from, to, StringComparison.OrdinalIgnoreCase)
        || (SafeWidening.TryGetValue(from, out var targets) && targets.Contains(to, StringComparer.OrdinalIgnoreCase));

    private static string SlugColumn(string label)
    {
        var slug = new string((label ?? string.Empty).ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        while (slug.Contains("__", StringComparison.Ordinal)) slug = slug.Replace("__", "_");
        slug = slug.Trim('_');
        if (slug.Length == 0) slug = "field";
        if (slug.Length > 58) slug = slug[..58].TrimEnd('_');
        return "cf_" + slug;
    }

    private static string Normalise(string column)
    {
        var trimmed = column.Trim().ToLowerInvariant();
        return trimmed.StartsWith("cf_", StringComparison.Ordinal) ? trimmed : "cf_" + trimmed.TrimStart('_');
    }

    private static string? ValidateColumnName(string column)
    {
        if (column.Length > 61) return "That column name is too long — 61 characters at most.";
        foreach (var c in column)
        {
            if (!char.IsLetterOrDigit(c) && c != '_')
                return "A column name may hold lowercase letters, digits and underscores only.";
            if (char.IsUpper(c))
                return "A column name may hold lowercase letters, digits and underscores only.";
        }
        return null;
    }

    private static FieldColumnControlType Control(string type, string label) => new()
    {
        ControlType = type,
        Label = label,
        SqlType = SqlTypes[type],
        HasOptions = type is "dropdown" or "radio" or "multiselect",
        IsNumeric = type is "number" or "decimal",
        IsDate = type is "date" or "datetime",
    };
}
