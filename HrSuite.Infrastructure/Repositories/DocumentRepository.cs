using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Config;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class DocumentRepository : RepositoryBase, IDocumentRepository
{
    public DocumentRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<DocumentListItem>> ListAsync(
        PageRequest page, string? status, int? employeeId, CancellationToken ct = default)
        => QueryPagedAsync<DocumentListItem>(
            "sp_hr_document_list",
            page,
            ProcArgs.New()
                .Set("status", string.IsNullOrWhiteSpace(status) ? null : status)
                .Set("employee_id", employeeId),
            ct);

    public Task<Document?> GetAsync(int documentId, CancellationToken ct = default)
        => QuerySingleAsync<Document>(
            "sp_hr_document_get",
            ProcArgs.New().Set("document_id", documentId),
            ct);

    public Task<Document?> SaveAsync(Document document, CancellationToken ct = default)
        => ExecuteReturningAsync<Document>(
            "sp_hr_document_save",
            ProcArgs.New()
                .Set("document_id", document.DocumentId)
                // Blank asks the procedure to mint one. Tenant-scoped numbering has to be
                // decided next to the rows it counts, not in application code racing itself.
                .Set("ref_no", string.IsNullOrWhiteSpace(document.RefNo) ? null : document.RefNo.Trim())
                .Set("employee_id", document.EmployeeId)
                .Set("document_type", document.DocumentType)
                .Set("template_id", document.TemplateId)
                .Set("subject", document.Subject)
                .Set("body_text", document.BodyText)
                .Set("effective_date", document.EffectiveDate)
                .Set("valid_till", document.ValidTill)
                .Set("signed_by", document.SignedBy)
                .Set("status", document.Status),
            ct);

    public Task<Document?> SetStatusAsync(
        int documentId, string status, string? deliveredVia, string? payloadJson, CancellationToken ct = default)
        => ExecuteReturningAsync<Document>(
            "sp_hr_document_status_set",
            ProcArgs.New()
                .Set("document_id", documentId)
                .Set("status", status)
                .Set("delivered_via", deliveredVia)
                .Set("payload_json", payloadJson),
            ct);

    public async Task<DocumentPrintContext?> PrintContextAsync(int documentId, CancellationToken ct = default)
    {
        var (rows, values) = await QueryTwoAsync<PrintContextRow, CustomValue>(
            "sp_hr_document_print_context",
            ProcArgs.New().Set("document_id", documentId),
            ct).ConfigureAwait(false);

        if (rows.Count == 0) return null;

        var row = rows[0];

        return new DocumentPrintContext
        {
            Document = new Document
            {
                DocumentId = row.DocumentId,
                RefNo = row.RefNo,
                EmployeeId = row.EmployeeId,
                DocumentType = row.DocumentType,
                TemplateId = row.TemplateId,
                Subject = row.Subject,
                BodyText = row.BodyText,
                EffectiveDate = row.EffectiveDate,
                ValidTill = row.ValidTill,
                SignedBy = row.SignedBy,
                Status = row.Status,
                IssuedOn = row.IssuedOn,
                AcknowledgedOn = row.AcknowledgedOn,
                DeliveredVia = row.DeliveredVia,
                PayloadJson = row.PayloadJson,
                EmployeeCode = row.EmployeeCode,
                EmployeeName = row.EmployeeName,
                DepartmentName = row.DepartmentName,
                DesignationName = row.DesignationName
            },
            Employee = new DocumentPrintEmployee
            {
                EmployeeId = row.EmployeeId,
                EmployeeCode = row.EmployeeCode,
                EmployeeName = row.EmployeeName,
                Dob = row.Dob,
                DateOfJoining = row.DateOfJoining,
                Mobile = row.Mobile,
                Email = row.Email,
                EmploymentStatus = row.EmploymentStatus,
                GrossCtc = row.GrossCtc,
                Hra = row.Hra,
                Tds = row.Tds,
                NetSalary = row.NetSalary,
                DepartmentName = row.DepartmentName,
                DesignationName = row.DesignationName
            },
            TenantName = row.TenantName,
            CustomValues = values.ToList()
        };
    }

    public async Task<DocumentStats> StatsAsync(CancellationToken ct = default)
    {
        var (totals, byType, byMonth) = await QueryThreeAsync<DocumentStats, DocumentTypeCount, DocumentPeriodCount>(
            "sp_hr_document_stats", ct: ct).ConfigureAwait(false);

        // An empty register still answers: the aggregate row exists, it is just all zeroes.
        var stats = totals.FirstOrDefault() ?? new DocumentStats();
        stats.ByType = byType.ToList();
        stats.ByMonth = byMonth.ToList();
        return stats;
    }

    public Task DeleteAsync(int documentId, CancellationToken ct = default)
        => ExecuteAsync("sp_hr_document_delete", ProcArgs.New().Set("document_id", documentId), ct);

    /// <summary>
    /// The print procedure joins the letter to its employee and to the tenant, so its first
    /// result set is one wide row. Splitting it into the shaped context is this repository's
    /// job — the service and the client both want the pieces named, not the join.
    /// </summary>
    private sealed class PrintContextRow
    {
        public int DocumentId { get; set; }
        public string RefNo { get; set; } = string.Empty;
        public int EmployeeId { get; set; }
        public string DocumentType { get; set; } = string.Empty;
        public int? TemplateId { get; set; }
        public string? Subject { get; set; }
        public string? BodyText { get; set; }
        public DateTime? EffectiveDate { get; set; }
        public DateTime? ValidTill { get; set; }
        public string? SignedBy { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? IssuedOn { get; set; }
        public DateTime? AcknowledgedOn { get; set; }
        public string? DeliveredVia { get; set; }
        public string? PayloadJson { get; set; }

        public string? EmployeeCode { get; set; }
        public string? EmployeeName { get; set; }
        public DateTime? Dob { get; set; }
        public DateTime? DateOfJoining { get; set; }
        public string? Mobile { get; set; }
        public string? Email { get; set; }
        public string? EmploymentStatus { get; set; }
        public decimal? GrossCtc { get; set; }
        public decimal? Hra { get; set; }
        public decimal? Tds { get; set; }
        public decimal? NetSalary { get; set; }
        public string? DepartmentName { get; set; }
        public string? DesignationName { get; set; }
        public string? TenantName { get; set; }
    }
}
