namespace HrSuite.Core.Domain.Config;

/// <summary>
/// A field the product never compiled in.
///
/// cfg_field_rule already lets a tenant hide, rename and reorder a field base code declared.
/// This is the other half: a field that exists for one tenant only. The two never collide —
/// a rule row keys on a compiled field key, a custom field declares its own.
///
/// Values live in rows rather than in new columns because every tenant shares hr_employee;
/// a column added for one tenant would appear on every other tenant's records.
/// </summary>
public sealed class CustomField
{
    public int FieldId { get; set; }

    /// <summary>Matches a screen key in <see cref="Extensibility.ScreenCatalog"/>.</summary>
    public string ScreenKey { get; set; } = string.Empty;

    /// <summary>
    /// The payload key. Fixed once created: a template and a stored script both reference a
    /// field by name, and renaming it would break them silently.
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;
    public string ControlType { get; set; } = "text";
    public bool IsRequired { get; set; }
    public string? DefaultValue { get; set; }
    public string? RangeMin { get; set; }
    public string? RangeMax { get; set; }
    public int? MaxLength { get; set; }
    public string? RegexPattern { get; set; }
    public string? HelpText { get; set; }
    public string? Placeholder { get; set; }
    public string? SectionKey { get; set; }

    /// <summary>Defaults above 1000 so a custom field lands after the compiled ones.</summary>
    public int SeqNo { get; set; } = 1000;

    public string Width { get; set; } = "half";

    /// <summary>None | Static | Lookup | Dynamic</summary>
    public string DataSourceType { get; set; } = "None";

    /// <summary>Which built-in lookup feeds the list when DataSourceType is Lookup.</summary>
    public string? LookupKey { get; set; }

    /// <summary>Cascading list: the field whose value filters this one's options.</summary>
    public string? ParentFieldKey { get; set; }

    // -----------------------------------------------------------------
    // Computed fields
    // -----------------------------------------------------------------

    /// <summary>Manual | Computed.</summary>
    public string ValueMode { get; set; } = "Manual";

    /// <summary>Authored against field keys in braces: "{basicPay} * 0.10".</summary>
    public string? FormulaText { get; set; }

    /// <summary>
    /// The keys the formula resolved to, cached on save. It lets a dependant be found
    /// without re-parsing every formula on the screen, and it is derived — never trusted
    /// from the caller.
    /// </summary>
    public string? FormulaRefsCsv { get; set; }

    /// <summary>Decimal places for the result. Null leaves it alone.</summary>
    public int? RoundTo { get; set; }

    /// <summary>
    /// Always recomputes on every save, so the control is read-only. Prefill only fills a
    /// blank, which lets the user override what was suggested.
    /// </summary>
    public string RecalcMode { get; set; } = "Always";

    public bool ShowInForm { get; set; } = true;
    public bool ShowInDetail { get; set; } = true;
    public bool ShowInPrint { get; set; } = true;

    public List<CustomFieldOption> Options { get; set; } = new();

    /// <summary>Set when DataSourceType is Dynamic. Null otherwise.</summary>
    public CustomFieldBinding? Binding { get; set; }
}

/// <summary>
/// Where a dynamic dropdown gets its options.
///
/// The endpoint is never free text — it is a row in <see cref="DataSource"/>. A
/// configuration screen that accepted a URL would be an SSRF, so the UI can only pick a
/// registered source and this binding says how to read it.
/// </summary>
public sealed class CustomFieldBinding
{
    public int BindingId { get; set; }
    public int FieldId { get; set; }
    public int SourceId { get; set; }

    /// <summary>Where the rows live in the payload, e.g. "data.items". Blank means the root.</summary>
    public string? ResultPath { get; set; }

    public string ValueField { get; set; } = string.Empty;
    public string LabelField { get; set; } = string.Empty;

    /// <summary>"{deptName} - {deptCode}". Overrides LabelField when set.</summary>
    public string? LabelTemplate { get; set; }

    public string? StaticParamsJson { get; set; }
    public string? SearchParamName { get; set; }

    /// <summary>Cascading list: the field whose value filters this one.</summary>
    public string? ParentFieldKey { get; set; }
    public string? ParentParamName { get; set; }

    public int CacheSeconds { get; set; } = 300;

    // Resolved from the source on read, so the editor can describe the binding without a
    // second call.
    public string? SourceCode { get; set; }
    public string? SourceName { get; set; }
    public string? SourceType { get; set; }
    public string? SourceKey { get; set; }
    public string? RelativeUrl { get; set; }
    public string? HttpMethod { get; set; }
    public bool RequiresParent { get; set; }
}

/// <summary>
/// One entry in the allowlist of places a dropdown may read from. Seeded by script, never
/// writable from the UI — that is what keeps it an allowlist rather than a suggestion.
/// </summary>
public sealed class DataSource
{
    public int SourceId { get; set; }
    public string SourceCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Lookup | NamedQuery | Api.</summary>
    public string SourceType { get; set; } = "Lookup";

    /// <summary>The lookup key or the registered named-query key, depending on the type.</summary>
    public string? SourceKey { get; set; }

    /// <summary>Api only: a path inside this API, resolved by the browser with its own token.</summary>
    public string? RelativeUrl { get; set; }

    public string HttpMethod { get; set; } = "GET";
    public string? DefaultResultPath { get; set; }
    public string? DefaultValueField { get; set; }
    public string? DefaultLabelField { get; set; }
    public bool RequiresParent { get; set; }
}

/// <summary>A resolved choice, as a dropdown renders it.</summary>
public sealed class FieldOption
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}

/// <summary>
/// What the browser gets back when it asks a source for options.
///
/// A Lookup or NamedQuery source is resolved here and comes back as rows. An Api source
/// cannot be: this server would have to call itself, and the call has to carry the caller's
/// own token and permissions. So the answer says "resolve this yourself" and hands over the
/// registered path — never a path the caller supplied.
/// </summary>
public sealed class FieldOptionsResult
{
    public bool ResolveOnClient { get; set; }
    public List<FieldOption> Options { get; set; } = new();

    public string? RelativeUrl { get; set; }
    public string? ResultPath { get; set; }
    public string? ValueField { get; set; }
    public string? LabelField { get; set; }
    public string? LabelTemplate { get; set; }
    public string? StaticParams { get; set; }
    public string? SearchParamName { get; set; }
    public string? ParentFieldKey { get; set; }
    public string? ParentParamName { get; set; }
}

/// <summary>"Test and load fields" — what a source actually returns, so the value and label
/// pickers are populated from reality rather than from guesswork.</summary>
public sealed class SourceProbeRequest
{
    public int SourceId { get; set; }
    public string? ResultPath { get; set; }
    public string? Search { get; set; }
    public string? ParentValue { get; set; }
}

public sealed class SourceProbeResult
{
    public bool ProbeOnClient { get; set; }
    public string? RelativeUrl { get; set; }
    public string? ResultPath { get; set; }
    public List<string> Columns { get; set; } = new();
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public string? SuggestedValueField { get; set; }
    public string? SuggestedLabelField { get; set; }
    public bool RequiresParent { get; set; }
    public string? Error { get; set; }
}

/// <summary>Validates a formula and runs it over sample values.</summary>
public sealed class FormulaTestRequest
{
    public string ScreenKey { get; set; } = string.Empty;
    public string? FieldKey { get; set; }
    public string FormulaText { get; set; } = string.Empty;
    public int? RoundTo { get; set; }
    public Dictionary<string, string?> SampleValues { get; set; } = new();
}

public sealed class FormulaTestResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public decimal? Value { get; set; }

    /// <summary>The keys the formula reads, in first-seen order.</summary>
    public List<string> Refs { get; set; } = new();

    /// <summary>Keys that had no value, which counted as zero.</summary>
    public List<string> Missing { get; set; } = new();

    /// <summary>The formula with each reference replaced by its label, for reading back.</summary>
    public string? Readable { get; set; }
}

/// <summary>
/// One change to a field definition.
///
/// The reference implementation audits DDL, because adding a field there runs ALTER TABLE.
/// Nothing is altered here, so what is worth recording is the definition itself — before
/// and after — which is the thing that actually changed.
/// </summary>
public sealed class CustomFieldAudit
{
    public int AuditId { get; set; }
    public string? ScreenKey { get; set; }
    public int? FieldId { get; set; }
    public string? FieldKey { get; set; }

    /// <summary>ADD | UPDATE | DELETE | REORDER.</summary>
    public string Action { get; set; } = string.Empty;

    public string? BeforeJson { get; set; }
    public string? AfterJson { get; set; }
    public string? PerformedByName { get; set; }
    public DateTime PerformedOn { get; set; }
    public bool Success { get; set; } = true;
    public string? ErrorText { get; set; }
}

/// <summary>A value kept from the moment its field was removed.</summary>
public sealed class CustomValueArchiveRow
{
    public int ArchiveId { get; set; }
    public string ScreenKey { get; set; } = string.Empty;
    public int? FieldId { get; set; }
    public string? FieldKey { get; set; }
    public int RecordId { get; set; }
    public string? ValueText { get; set; }
    public DateTime DroppedOn { get; set; }
}

public sealed class CustomFieldOption
{
    public int OptionId { get; set; }
    public int FieldId { get; set; }
    public string OptionValue { get; set; } = string.Empty;
    public string OptionLabel { get; set; } = string.Empty;

    /// <summary>Null means the option shows whatever the parent field holds.</summary>
    public string? ParentValue { get; set; }

    public int SeqNo { get; set; }
}

/// <summary>One custom field's value for one record, as the form sees it.</summary>
public sealed class CustomValue
{
    public int FieldId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string ControlType { get; set; } = "text";
    public bool ShowInForm { get; set; } = true;
    public bool ShowInDetail { get; set; } = true;
    public bool ShowInPrint { get; set; } = true;
    public int SeqNo { get; set; }
    public string? ValueText { get; set; }
}

/// <summary>The write side: what a form posts after its own record has an id.</summary>
public sealed class CustomValueSaveRequest
{
    public string ScreenKey { get; set; } = string.Empty;
    public int RecordId { get; set; }
    public List<CustomValueEntry> Values { get; set; } = new();
}

public sealed class CustomValueEntry
{
    public string FieldKey { get; set; } = string.Empty;
    public string? ValueText { get; set; }
}

/// <summary>A drag-and-drop reorder, sent as one batch rather than one call per row.</summary>
public sealed class CustomFieldOrderEntry
{
    public int FieldId { get; set; }
    public int SeqNo { get; set; }
    public string? SectionKey { get; set; }
}

/// <summary>
/// How many records already hold a value for a field. Shown before a delete, so removing a
/// field is an informed choice rather than a surprise.
/// </summary>
public sealed class CustomFieldUsage
{
    public int FilledCount { get; set; }
}

/// <summary>A screen that accepts custom fields, plus the keys it already compiled in.</summary>
public sealed class CustomFieldScreen
{
    public string ScreenKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int FieldCount { get; set; }
    public List<string> CompiledFieldKeys { get; set; } = new();
}

/// <summary>The control types the builder offers, and what each one may be configured with.</summary>
public sealed class CustomControlType
{
    public string ControlType { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public bool HasOptions { get; set; }
    public bool IsNumeric { get; set; }
    public bool IsDate { get; set; }
}
