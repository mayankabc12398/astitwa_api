using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 rules for an issued document — true for every customer:
///
///   * A document belongs to an employee and to a document type the catalogue knows.
///   * Status moves forward through a fixed set. A revoked letter does not become issued
///     again, and an acknowledgement cannot precede an issue.
///   * What was printed is captured once, at issue. Editing the template afterwards must
///     not change what a document already said.
///
/// The wording of a letter is not a Layer 1 concern: the body is the tenant's text and the
/// layout is their template.
/// </summary>
public sealed class DocumentService : IDocumentService
{
    /// <summary>
    /// Which statuses each one may move to. Draft and Pending Signature are still being
    /// prepared; Issued has left the building, so from there the only moves are the
    /// recipient acknowledging, the term running out, or the employer withdrawing it.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> Transitions =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [DocumentStatus.Draft] = new[] { DocumentStatus.PendingSignature, DocumentStatus.Issued, DocumentStatus.Revoked },
            [DocumentStatus.PendingSignature] = new[] { DocumentStatus.Draft, DocumentStatus.Issued, DocumentStatus.Revoked },
            [DocumentStatus.Issued] = new[] { DocumentStatus.Acknowledged, DocumentStatus.Expired, DocumentStatus.Revoked },
            [DocumentStatus.Acknowledged] = new[] { DocumentStatus.Expired, DocumentStatus.Revoked },
            [DocumentStatus.Expired] = new[] { DocumentStatus.Revoked },
            [DocumentStatus.Revoked] = Array.Empty<string>()
        };

    private readonly IDocumentRepository _repository;
    private readonly IPrintTemplateRepository _templates;
    private readonly HookInvoker _hooks;

    public DocumentService(
        IDocumentRepository repository,
        IPrintTemplateRepository templates,
        HookInvoker hooks)
    {
        _repository = repository;
        _templates = templates;
        _hooks = hooks;
    }

    public Task<PagedResult<DocumentListItem>> ListAsync(
        PageRequest page, string? status, int? employeeId, CancellationToken ct = default)
        => _repository.ListAsync(page, status, employeeId, ct);

    public async Task<Result<Document>> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        return found is null ? Result<Document>.NotFound("Document not found.") : Result<Document>.Success(found);
    }

    public async Task<Result<Document>> SaveAsync(Document document, CancellationToken ct = default)
    {
        var validation = new Validator()
            .Require(document.EmployeeId > 0, "Employee is required.", "employeeId")
            .RequireText(document.DocumentType, "Document type is required.", "documentType")
            .ToResult();

        if (validation.IsFailure) return Result<Document>.Fail(validation.Errors.ToArray());

        document.DocumentType = document.DocumentType.Trim();
        document.Subject = document.Subject?.Trim();
        document.SignedBy = document.SignedBy?.Trim();

        var known = await _templates.DocumentTypesAsync(ct).ConfigureAwait(false);
        if (!known.Any(d => string.Equals(d.DocumentType, document.DocumentType, StringComparison.OrdinalIgnoreCase)))
        {
            return Result<Document>.Invalid(
                $"'{document.DocumentType}' is not a document type this product issues.", "documentType");
        }

        // A range that runs backwards is the same Layer 1 mistake the leave screen guards.
        if (document.EffectiveDate is { } from && document.ValidTill is { } till && till.Date < from.Date)
        {
            return Result<Document>.Invalid("Valid-till cannot be earlier than the effective date.", "validTill");
        }

        if (document.DocumentId > 0)
        {
            var existing = await _repository.GetAsync(document.DocumentId, ct).ConfigureAwait(false);
            if (existing is null) return Result<Document>.NotFound("Document not found.");

            // Once a letter is out, its content is a record of what was sent. Withdraw it
            // and issue a new one instead of rewriting history.
            if (!IsEditable(existing.Status))
            {
                return Result<Document>.Invalid(
                    $"A document that is {existing.Status.ToLowerInvariant()} can no longer be edited. Revoke it and issue a new one.");
            }

            document.Status = existing.Status;
        }
        else if (string.IsNullOrWhiteSpace(document.Status))
        {
            document.Status = DocumentStatus.Draft;
        }

        if (!DocumentStatus.All.Contains(document.Status, StringComparer.OrdinalIgnoreCase))
        {
            return Result<Document>.Invalid($"'{document.Status}' is not a status a document can hold.", "status");
        }

        var saved = await _repository.SaveAsync(document, ct).ConfigureAwait(false);
        if (saved is null) return Result<Document>.NotFound("Document not found.");

        await _hooks.RunAsync(HookKeyFor(saved), form: document, response: saved, ct: ct).ConfigureAwait(false);

        return Result<Document>.Success(saved);
    }

    public async Task<Result<Document>> ChangeStatusAsync(DocumentStatusChange change, CancellationToken ct = default)
    {
        if (change.DocumentId <= 0) return Result<Document>.Invalid("Document is required.", "documentId");
        if (string.IsNullOrWhiteSpace(change.Status)) return Result<Document>.Invalid("Status is required.", "status");

        var target = DocumentStatus.All.FirstOrDefault(
            s => string.Equals(s, change.Status.Trim(), StringComparison.OrdinalIgnoreCase));

        if (target is null)
        {
            return Result<Document>.Invalid($"'{change.Status}' is not a status a document can hold.", "status");
        }

        var existing = await _repository.GetAsync(change.DocumentId, ct).ConfigureAwait(false);
        if (existing is null) return Result<Document>.NotFound("Document not found.");

        if (string.Equals(existing.Status, target, StringComparison.OrdinalIgnoreCase))
        {
            return Result<Document>.Success(existing);
        }

        var allowed = Transitions.TryGetValue(existing.Status, out var moves) ? moves : Array.Empty<string>();
        if (!allowed.Contains(target, StringComparer.OrdinalIgnoreCase))
        {
            return Result<Document>.Invalid(
                $"A {existing.Status.ToLowerInvariant()} document cannot become {target.ToLowerInvariant()}.", "status");
        }

        // The snapshot only travels on the way to Issued, and the procedure keeps the first
        // one it is given, so a re-issue cannot quietly replace what was sent.
        var payload = string.Equals(target, DocumentStatus.Issued, StringComparison.OrdinalIgnoreCase)
            ? change.PayloadJson
            : null;

        var updated = await _repository
            .SetStatusAsync(change.DocumentId, target, change.DeliveredVia?.Trim(), payload, ct)
            .ConfigureAwait(false);

        return updated is null
            ? Result<Document>.NotFound("Document not found.")
            : Result<Document>.Success(updated);
    }

    public async Task<Result<DocumentPrintContext>> PrintContextAsync(int id, CancellationToken ct = default)
    {
        var context = await _repository.PrintContextAsync(id, ct).ConfigureAwait(false);
        return context is null
            ? Result<DocumentPrintContext>.NotFound("Document not found.")
            : Result<DocumentPrintContext>.Success(context);
    }

    public Task<DocumentStats> StatsAsync(CancellationToken ct = default) => _repository.StatsAsync(ct);

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        var existing = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        if (existing is null) return Result.Fail(Error.NotFound("Document not found."));

        if (!IsEditable(existing.Status))
        {
            return Result.Invalid(
                $"A document that is {existing.Status.ToLowerInvariant()} cannot be deleted. Revoke it instead.");
        }

        await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private static bool IsEditable(string status)
        => string.Equals(status, DocumentStatus.Draft, StringComparison.OrdinalIgnoreCase)
        || string.Equals(status, DocumentStatus.PendingSignature, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Documents have no compiled hook slot of their own yet, so a script that wants to act
    /// on one listens on the employee's after-save slot. Naming the key here rather than in
    /// HookKeys keeps that a fact about this service, not a promise in the catalogue.
    /// </summary>
    private static string HookKeyFor(Document document) => $"hr.document.{document.DocumentType}.afterSave";
}
