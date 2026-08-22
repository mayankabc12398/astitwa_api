using System.Text.RegularExpressions;
using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 rules for a print template — true for every customer:
///
///   * A template belongs to a document type the catalogue knows about.
///   * A block type and a field format come from the compiled vocabularies below. The
///     designer offers only these, and the server checks again, because a hand-built
///     payload naming an unknown block would render as nothing at all.
///   * A seeded system template can be edited but never deleted, so a document type always
///     resolves to something printable.
///
/// What a letter says, which blocks it carries and how it looks are all the tenant's
/// decision. None of that is validated here beyond the vocabulary.
/// </summary>
public sealed class PrintTemplateService : IPrintTemplateService
{
    private static readonly HashSet<string> SectionTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Header", "Title", "RefDate", "Addressee", "Subject", "Paragraphs", "FieldGrid",
        "Table", "RichText", "SignOff", "Signature", "Spacer", "PageBreak", "QrCode", "Footer"
    };

    private static readonly HashSet<string> Formats = new(StringComparer.OrdinalIgnoreCase)
    {
        "text", "date", "datetime", "currency", "number"
    };

    private static readonly HashSet<string> Aligns = new(StringComparer.OrdinalIgnoreCase)
    {
        "left", "center", "right"
    };

    private static readonly HashSet<string> BorderStyles = new(StringComparer.OrdinalIgnoreCase)
    {
        "none", "box", "underline", "grid"
    };

    private static readonly HashSet<string> PageSizes = new(StringComparer.OrdinalIgnoreCase)
    {
        "A4", "A5", "Letter", "Legal"
    };

    /// <summary>Upper case, digits and dashes. It ends up in a unique index and in a URL.</summary>
    private static readonly Regex CodeShape = new("^[A-Z0-9][A-Z0-9-]{1,79}$", RegexOptions.Compiled);

    private readonly IPrintTemplateRepository _repository;

    public PrintTemplateService(IPrintTemplateRepository repository) => _repository = repository;

    public Task<IReadOnlyList<PrintDocumentType>> DocumentTypesAsync(CancellationToken ct = default)
        => _repository.DocumentTypesAsync(ct);

    public Task<PagedResult<PrintTemplateListItem>> ListAsync(
        PageRequest page, string? documentType, CancellationToken ct = default)
        => _repository.ListAsync(page, documentType, ct);

    public async Task<Result<PrintTemplate>> GetAsync(int templateId, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(templateId, ct).ConfigureAwait(false);
        return found is null
            ? Result<PrintTemplate>.NotFound("Template not found.")
            : Result<PrintTemplate>.Success(found);
    }

    public async Task<Result<PrintTemplate>> ResolveAsync(string documentType, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(documentType))
            return Result<PrintTemplate>.Invalid("Document type is required.", "documentType");

        var found = await _repository.ResolveAsync(documentType.Trim(), ct).ConfigureAwait(false);

        // Not an error the caller should treat as a fault: a tenant that has configured
        // nothing prints with the client's built-in layout, which is how this feature stays
        // invisible until somebody uses it.
        return found is null
            ? Result<PrintTemplate>.NotFound($"No template is configured for '{documentType}'.")
            : Result<PrintTemplate>.Success(found);
    }

    public Task<IReadOnlyList<PrintAvailableField>> AvailableFieldsAsync(string documentType, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(documentType)
            ? Task.FromResult<IReadOnlyList<PrintAvailableField>>(Array.Empty<PrintAvailableField>())
            : _repository.AvailableFieldsAsync(documentType.Trim(), ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(string? documentType, CancellationToken ct = default)
        => _repository.LookupAsync(documentType, ct);

    public async Task<Result<PrintTemplateListItem>> SaveAsync(PrintTemplate template, CancellationToken ct = default)
    {
        var validation = new Validator()
            .RequireText(template.TemplateName, "Template name is required.", "templateName")
            .RequireText(template.DocumentType, "Document type is required.", "documentType")
            .ToResult();

        if (validation.IsFailure) return Result<PrintTemplateListItem>.Fail(validation.Errors.ToArray());

        template.TemplateName = template.TemplateName.Trim();
        template.DocumentType = template.DocumentType.Trim();

        var known = await _repository.DocumentTypesAsync(ct).ConfigureAwait(false);
        if (!known.Any(d => string.Equals(d.DocumentType, template.DocumentType, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<PrintTemplateListItem>.Invalid(
                $"'{template.DocumentType}' is not a document type this product can print.", "documentType");
        }

        // A new template needs a code and the caller rarely has one worth keeping; derive it
        // from the name so the row is identifiable in the database without a UI field for it.
        if (template.TemplateId == 0 && string.IsNullOrWhiteSpace(template.TemplateCode))
        {
            template.TemplateCode = CodeFrom(template.DocumentType, template.TemplateName);
        }

        if (template.TemplateId == 0 && !CodeShape.IsMatch(template.TemplateCode))
        {
            return Result<PrintTemplateListItem>.Invalid(
                "Template code may contain upper-case letters, digits and dashes only.", "templateCode");
        }

        var blocks = Normalise(template);
        if (blocks.IsFailure) return Result<PrintTemplateListItem>.Fail(blocks.Errors.ToArray());

        var saved = await _repository.SaveAsync(template, ct).ConfigureAwait(false);
        return saved is null
            ? Result<PrintTemplateListItem>.NotFound("Template not found.")
            : Result<PrintTemplateListItem>.Success(saved);
    }

    public async Task<Result<PrintTemplateListItem>> CloneAsync(
        PrintTemplateCloneRequest request, CancellationToken ct = default)
    {
        if (request.TemplateId <= 0) return Result<PrintTemplateListItem>.Invalid("Template is required.", "templateId");
        if (string.IsNullOrWhiteSpace(request.TemplateName))
            return Result<PrintTemplateListItem>.Invalid("A name for the copy is required.", "templateName");

        var source = await _repository.GetAsync(request.TemplateId, ct).ConfigureAwait(false);
        if (source is null) return Result<PrintTemplateListItem>.NotFound("Template not found.");

        var clone = await _repository.CloneAsync(request.TemplateId, request.TemplateName.Trim(), ct).ConfigureAwait(false);
        return clone is null
            ? Result<PrintTemplateListItem>.NotFound("Template not found.")
            : Result<PrintTemplateListItem>.Success(clone);
    }

    public async Task<Result<PrintTemplateListItem>> SetDefaultAsync(int templateId, CancellationToken ct = default)
    {
        var updated = await _repository.SetDefaultAsync(templateId, ct).ConfigureAwait(false);
        return updated is null
            ? Result<PrintTemplateListItem>.NotFound("Template not found.")
            : Result<PrintTemplateListItem>.Success(updated);
    }

    public async Task<Result> DeleteAsync(int templateId, CancellationToken ct = default)
    {
        var existing = await _repository.GetAsync(templateId, ct).ConfigureAwait(false);
        if (existing is null) return Result.Fail(Error.NotFound("Template not found."));

        if (existing.IsSystem)
        {
            return Result.Invalid(
                "The standard template for a document type cannot be deleted. Edit it, or make another one the default.");
        }

        var affected = await _repository.DeleteAsync(templateId, ct).ConfigureAwait(false);
        return affected == 0
            ? Result.Fail(Error.NotFound("Template not found."))
            : Result.Success();
    }

    /// <summary>
    /// Trims the tree to the compiled vocabulary and renumbers it. Sequence numbers arrive
    /// from a drag-and-drop list where they are whatever the browser last wrote; the order
    /// is what matters, so it is re-derived from the array rather than trusted.
    /// </summary>
    private static Result Normalise(PrintTemplate template)
    {
        template.PageSize = PageSizes.Contains(template.PageSize) ? template.PageSize : "A4";
        template.Orientation = string.Equals(template.Orientation, "landscape", StringComparison.OrdinalIgnoreCase)
            ? "landscape"
            : "portrait";

        var errors = new Validator();
        var seq = 0;

        foreach (var section in template.Sections)
        {
            if (!SectionTypes.Contains(section.SectionType))
            {
                errors.Require(false, $"'{section.SectionType}' is not a block this product can print.", "sections");
                continue;
            }

            seq += 10;
            section.SeqNo = seq;
            section.ColumnCount = Math.Clamp(section.ColumnCount, 1, 4);
            section.PaddingMm = Math.Clamp(section.PaddingMm, 0m, 50m);
            section.BorderStyle = BorderStyles.Contains(section.BorderStyle) ? section.BorderStyle : "none";

            var fieldSeq = 0;
            foreach (var field in section.Fields)
            {
                if (string.IsNullOrWhiteSpace(field.FieldKey))
                {
                    errors.Require(false, "A field in a block has no key.", "sections");
                    continue;
                }

                fieldSeq += 10;
                field.SeqNo = fieldSeq;
                field.FieldKey = field.FieldKey.Trim();
                field.WidthPercent = Math.Clamp(field.WidthPercent, 5, 100);
                field.Align = Aligns.Contains(field.Align) ? field.Align : "left";
                field.Format = Formats.Contains(field.Format) ? field.Format : "text";
            }

            // A block whose fields were all rejected would silently print empty; drop the
            // ones that never had a key rather than storing them.
            section.Fields = section.Fields.Where(f => !string.IsNullOrWhiteSpace(f.FieldKey)).ToList();
        }

        template.Sections = template.Sections.Where(s => SectionTypes.Contains(s.SectionType)).ToList();
        return errors.ToResult();
    }

    private static string CodeFrom(string documentType, string name)
    {
        var slug = new string(name.ToUpperInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = Regex.Replace(slug, "-{2,}", "-").Trim('-');
        if (slug.Length > 40) slug = slug[..40].TrimEnd('-');
        if (slug.Length == 0) slug = "TEMPLATE";

        var prefix = documentType.ToUpperInvariant();
        if (prefix.Length > 30) prefix = prefix[..30];

        // The column is VARCHAR(80) and the value lands in a unique index; a name long
        // enough to overflow it would fail as a truncation error rather than as a message.
        var code = $"{prefix}-{slug}";
        return code.Length > 80 ? code[..80].TrimEnd('-') : code;
    }
}
