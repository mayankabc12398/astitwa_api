namespace HrSuite.Core.Domain.Config;

/// <summary>
/// A screen whose fields are real columns.
///
/// The Field Builder in <see cref="CustomField"/> stores a tenant's extra fields as values in
/// rows. This is the other model: the field IS a column on <see cref="BaseTable"/>, so it can
/// be indexed, joined and reported on like any column the product shipped. The cost is that a
/// change here is a schema change — which is why the table and its key are read from the
/// registry and never from a request.
/// </summary>
public sealed class FieldColumnScreen
{
    public int ScreenId { get; set; }
    public string ScreenCode { get; set; } = string.Empty;
    public string ScreenName { get; set; } = string.Empty;
    public string BaseTable { get; set; } = string.Empty;
    public string PkColumn { get; set; } = string.Empty;
    public string? ModuleName { get; set; }
    public string? RoutePath { get; set; }

    /// <summary>'Role,Compensation &amp; Timeline,Review' — the wizard's steps, in order.</summary>
    public string? StepLabelsCsv { get; set; }

    public int FieldCount { get; set; }
    public int CustomFieldCount { get; set; }

    public IReadOnlyList<string> Steps =>
        (StepLabelsCsv ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

/// <summary>
/// One field on such a screen.
///
/// IsCustom = false describes a column the product ships. Those rows are not editable and not
/// droppable through this feature; they exist so a new field can be placed BETWEEN two of
/// them rather than only after all of them.
/// </summary>
public sealed class FieldColumn
{
    public int FieldId { get; set; }
    public int ScreenId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>cf_… for a field this feature created; the real name for a shipped column.</summary>
    public string ColumnName { get; set; } = string.Empty;

    public string ControlType { get; set; } = "text";

    /// <summary>Resolved on the server from <see cref="ControlType"/>. A client-sent value is ignored.</summary>
    public string? SqlType { get; set; }

    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? RangeMin { get; set; }
    public string? RangeMax { get; set; }
    public int? MaxLength { get; set; }
    public string? RegexPattern { get; set; }
    public string? HelpText { get; set; }
    public string? Placeholder { get; set; }

    public int StepIndex { get; set; }
    public int SortOrder { get; set; }
    public string Width { get; set; } = "half";

    /// <summary>None | Static. Dynamic sources are the row-based builder's feature, not this one.</summary>
    public string DataSourceType { get; set; } = "None";

    public bool ShowInForm { get; set; } = true;
    public bool ShowInDetail { get; set; } = true;
    public bool ShowInPrint { get; set; } = true;
    public bool IsCustom { get; set; } = true;

    /// <summary>Where a new field goes: the field it follows, or 0 for the top of its step.</summary>
    public int AfterFieldId { get; set; }

    /// <summary>Filled by the single-field read, so a caller holding one field knows its screen.</summary>
    public string? ScreenCode { get; set; }

    public List<FieldColumnOption> Options { get; set; } = new();
}

public sealed class FieldColumnOption
{
    public int OptionId { get; set; }
    public int FieldId { get; set; }
    public string OptionValue { get; set; } = string.Empty;
    public string OptionLabel { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>A screen and everything drawn on it, in render order.</summary>
public sealed class FieldColumnLayout
{
    public FieldColumnScreen Screen { get; set; } = new();
    public List<string> Steps { get; set; } = new();
    public List<FieldColumn> Fields { get; set; } = new();
}

/// <summary>One statement this feature ran against the schema, successful or not.</summary>
public sealed class FieldColumnAudit
{
    public int AuditId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public string? ColumnName { get; set; }
    public string SqlText { get; set; } = string.Empty;
    public int? PerformedBy { get; set; }
    public DateTime PerformedOn { get; set; }
    public bool Success { get; set; }
    public string? ErrorText { get; set; }
    public string? ScreenCode { get; set; }
    public string? ScreenName { get; set; }
}

/// <summary>One field's place on the form, as the structure list left it.</summary>
public sealed class FieldColumnPosition
{
    public int FieldId { get; set; }
    public int StepIndex { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>What the builder may offer, and the column each choice produces.</summary>
public sealed class FieldColumnControlType
{
    public string ControlType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string SqlType { get; set; } = string.Empty;
    public bool HasOptions { get; set; }
    public bool IsNumeric { get; set; }
    public bool IsDate { get; set; }
}
