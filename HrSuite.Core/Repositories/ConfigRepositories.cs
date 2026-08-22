using HrSuite.Common.Results;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Hr;

namespace HrSuite.Core.Repositories;

/// <summary>
/// Data contracts for tenant screen configuration. Implementations live in
/// HrSuite.Infrastructure and stamp the tenant themselves — no method here takes a tenant
/// id, because no caller may supply one.
/// </summary>
public interface IPrintTemplateRepository
{
    Task<IReadOnlyList<PrintDocumentType>> DocumentTypesAsync(CancellationToken ct = default);
    Task<PagedResult<PrintTemplateListItem>> ListAsync(PageRequest page, string? documentType, CancellationToken ct = default);

    /// <summary>Returns null when the template does not exist for this tenant.</summary>
    Task<PrintTemplate?> GetAsync(int templateId, CancellationToken ct = default);

    /// <summary>Returns null when the tenant has configured no template for the type.</summary>
    Task<PrintTemplate?> ResolveAsync(string documentType, CancellationToken ct = default);

    Task<IReadOnlyList<PrintAvailableField>> AvailableFieldsAsync(string documentType, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(string? documentType, CancellationToken ct = default);
    Task<PrintTemplateListItem?> SaveAsync(PrintTemplate template, CancellationToken ct = default);
    Task<PrintTemplateListItem?> CloneAsync(int templateId, string templateName, CancellationToken ct = default);
    Task<PrintTemplateListItem?> SetDefaultAsync(int templateId, CancellationToken ct = default);

    /// <summary>Rows affected. Zero means the template was a seeded system one and stands.</summary>
    Task<int> DeleteAsync(int templateId, CancellationToken ct = default);
}

public interface ICustomFieldRepository
{
    Task<IReadOnlyList<CustomField>> ListAsync(string? screenKey, CancellationToken ct = default);
    Task<CustomField?> GetAsync(int fieldId, CancellationToken ct = default);
    Task<CustomField?> SaveAsync(CustomField field, CancellationToken ct = default);
    Task DeleteAsync(int fieldId, CancellationToken ct = default);
    Task ReorderAsync(IReadOnlyList<CustomFieldOrderEntry> items, CancellationToken ct = default);
    Task<CustomFieldUsage> UsageAsync(int fieldId, CancellationToken ct = default);
    Task<IReadOnlyList<CustomValue>> ValuesAsync(string screenKey, int recordId, CancellationToken ct = default);
    Task SaveValuesAsync(string screenKey, int recordId, IReadOnlyList<CustomValueEntry> values, CancellationToken ct = default);

    // ---- bound dropdowns -------------------------------------------------

    /// <summary>The allowlist a dropdown may bind to. Seeded, never written from the UI.</summary>
    Task<IReadOnlyList<DataSource>> DataSourcesAsync(CancellationToken ct = default);
    Task<DataSource?> DataSourceAsync(int sourceId, CancellationToken ct = default);
    Task SaveBindingAsync(int fieldId, CustomFieldBinding binding, CancellationToken ct = default);
    Task DeleteBindingAsync(int fieldId, CancellationToken ct = default);

    /// <summary>Resolves one of the product's own bounded lookups by key.</summary>
    Task<IReadOnlyList<LookupItem>> LookupAsync(string lookupKey, CancellationToken ct = default);

    // ---- computed fields -------------------------------------------------

    Task<IReadOnlyList<CustomField>> ComputedAsync(string screenKey, CancellationToken ct = default);

    // ---- audit and archive -----------------------------------------------

    Task<PagedResult<CustomFieldAudit>> AuditAsync(PageRequest page, string? screenKey, CancellationToken ct = default);

    Task AddAuditAsync(
        string? screenKey, int fieldId, string? fieldKey, string action,
        string? beforeJson, string? afterJson, bool success, string? errorText,
        CancellationToken ct = default);

    /// <summary>Copies every value a field holds into the archive. Rows archived.</summary>
    Task<int> ArchiveValuesAsync(int fieldId, CancellationToken ct = default);

    Task<PagedResult<CustomValueArchiveRow>> ArchiveAsync(PageRequest page, int? fieldId, CancellationToken ct = default);
}
