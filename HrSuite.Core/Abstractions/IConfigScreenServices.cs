using HrSuite.Common.Results;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Hr;   // LookupItem — one dropdown contract for the whole product

namespace HrSuite.Core.Abstractions;

/// <summary>
/// The print designer's server half. Templates are tenant configuration, so nothing here
/// takes a tenant id — the repository stamps it, exactly as it does for HR data.
/// </summary>
public interface IPrintTemplateService
{
    Task<IReadOnlyList<PrintDocumentType>> DocumentTypesAsync(CancellationToken ct = default);
    Task<PagedResult<PrintTemplateListItem>> ListAsync(PageRequest page, string? documentType, CancellationToken ct = default);
    Task<Result<PrintTemplate>> GetAsync(int templateId, CancellationToken ct = default);

    /// <summary>
    /// The template the runtime should print a document type with. Fails with NotFound when
    /// the tenant has configured none, and the client then falls back to its built-in
    /// layout — which is what keeps installing this feature from changing existing output.
    /// </summary>
    Task<Result<PrintTemplate>> ResolveAsync(string documentType, CancellationToken ct = default);

    Task<IReadOnlyList<PrintAvailableField>> AvailableFieldsAsync(string documentType, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(string? documentType, CancellationToken ct = default);
    Task<Result<PrintTemplateListItem>> SaveAsync(PrintTemplate template, CancellationToken ct = default);
    Task<Result<PrintTemplateListItem>> CloneAsync(PrintTemplateCloneRequest request, CancellationToken ct = default);
    Task<Result<PrintTemplateListItem>> SetDefaultAsync(int templateId, CancellationToken ct = default);
    Task<Result> DeleteAsync(int templateId, CancellationToken ct = default);
}

/// <summary>
/// The field builder's server half. It configures fields; it never runs DDL, because every
/// tenant shares the same tables and a column added for one would appear on all of them.
/// </summary>
public interface ICustomFieldService
{
    /// <summary>Which screens accept custom fields, read from the compiled screen catalogue.</summary>
    Task<IReadOnlyList<CustomFieldScreen>> ScreensAsync(CancellationToken ct = default);

    IReadOnlyList<CustomControlType> ControlTypes();

    Task<IReadOnlyList<CustomField>> ListAsync(string screenKey, CancellationToken ct = default);
    Task<Result<CustomField>> GetAsync(int fieldId, CancellationToken ct = default);
    Task<Result<CustomField>> SaveAsync(CustomField field, CancellationToken ct = default);
    Task<Result> DeleteAsync(int fieldId, CancellationToken ct = default);
    Task<Result> ReorderAsync(IReadOnlyList<CustomFieldOrderEntry> items, CancellationToken ct = default);
    Task<CustomFieldUsage> UsageAsync(int fieldId, CancellationToken ct = default);

    Task<IReadOnlyList<CustomValue>> ValuesAsync(string screenKey, int recordId, CancellationToken ct = default);

    /// <summary>
    /// Writes a record's custom values once its own save has returned an id. Every value is
    /// validated against its field first, so a required field left blank or a number typed
    /// as text is refused here rather than stored as junk.
    ///
    /// Computed fields are recalculated here, in dependency order, before anything is
    /// stored: the browser evaluates the same grammar for immediacy, but this is the
    /// answer that ends up in the database.
    /// </summary>
    Task<Result> SaveValuesAsync(CustomValueSaveRequest request, CancellationToken ct = default);

    // ---- computed fields -------------------------------------------------

    /// <summary>
    /// Validates a formula and runs it over sample values. Answers in one shape whether the
    /// formula was rejected by the parser or by the reference check, so the editor has one
    /// place to read the message from.
    /// </summary>
    Task<Result<FormulaTestResult>> TestFormulaAsync(FormulaTestRequest request, CancellationToken ct = default);

    // ---- bound dropdowns -------------------------------------------------

    /// <summary>The allowlist of places a dropdown may read from.</summary>
    Task<IReadOnlyList<DataSource>> DataSourcesAsync(CancellationToken ct = default);

    /// <summary>
    /// Resolves one field's options. A static list is answered from its own rows, a lookup
    /// or named query is resolved here, and an API source comes back as "resolve this
    /// yourself" with the registered path — this server calling itself would lose the
    /// caller's identity.
    /// </summary>
    Task<Result<FieldOptionsResult>> OptionsAsync(int fieldId, string? search, string? parentValue, CancellationToken ct = default);

    /// <summary>
    /// "Test and load fields": what a source actually returns, so the value and label
    /// pickers are filled from reality rather than from guesswork.
    /// </summary>
    Task<Result<SourceProbeResult>> ProbeAsync(SourceProbeRequest request, CancellationToken ct = default);

    // ---- audit and archive -----------------------------------------------

    Task<PagedResult<CustomFieldAudit>> AuditAsync(PageRequest page, string? screenKey, CancellationToken ct = default);
    Task<PagedResult<CustomValueArchiveRow>> ArchiveAsync(PageRequest page, int? fieldId, CancellationToken ct = default);
}
