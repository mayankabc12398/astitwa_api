namespace HrSuite.Core.Domain.Hr;

/// <summary>The statuses a document moves through. Free text nowhere: the service checks these.</summary>
public static class DocumentStatus
{
    public const string Draft = "Draft";
    public const string PendingSignature = "Pending Signature";
    public const string Issued = "Issued";
    public const string Acknowledged = "Acknowledged";
    public const string Expired = "Expired";
    public const string Revoked = "Revoked";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Draft, PendingSignature, Issued, Acknowledged, Expired, Revoked
    };
}

/// <summary>
/// A letter issued to an employee — offer, appointment, confirmation and the rest.
///
/// What was printed is captured in <see cref="PayloadJson"/> at issue and never rewritten,
/// so editing a template afterwards cannot change what a document already said.
/// </summary>
public sealed class Document
{
    public int DocumentId { get; set; }

    /// <summary>Tenant-unique and human-readable. Generated when the caller leaves it blank.</summary>
    public string RefNo { get; set; } = string.Empty;

    public int EmployeeId { get; set; }
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Null means "resolve the tenant's default for this type at print time".</summary>
    public int? TemplateId { get; set; }

    public string? Subject { get; set; }

    /// <summary>The letter's paragraphs, one per line.</summary>
    public string? BodyText { get; set; }

    public DateTime? EffectiveDate { get; set; }
    public DateTime? ValidTill { get; set; }
    public string? SignedBy { get; set; }
    public string Status { get; set; } = DocumentStatus.Draft;
    public DateTime? IssuedOn { get; set; }
    public DateTime? AcknowledgedOn { get; set; }
    public string? DeliveredVia { get; set; }
    public string? PayloadJson { get; set; }

    // Resolved on read so a form needs no second call.
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }
    public string? DepartmentName { get; set; }
    public string? DesignationName { get; set; }
}

/// <summary>List-row projection. Carries the resolved names so the grid needs no second call.</summary>
public sealed class DocumentListItem
{
    public int DocumentId { get; set; }
    public string RefNo { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string? EmployeeCode { get; set; }
    public string? EmployeeName { get; set; }
    public string? DepartmentName { get; set; }
    public string? DesignationName { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string? DocumentTypeName { get; set; }
    public string? Subject { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public DateTime? ValidTill { get; set; }
    public string? SignedBy { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime? IssuedOn { get; set; }
    public DateTime? AcknowledgedOn { get; set; }
    public string? DeliveredVia { get; set; }
    public DateTime? CreatedOn { get; set; }
}

/// <summary>A status move, with the snapshot the client rendered when it was an issue.</summary>
public sealed class DocumentStatusChange
{
    public int DocumentId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? DeliveredVia { get; set; }
    public string? PayloadJson { get; set; }
}

/// <summary>
/// Everything one document needs in order to print: the letter, the employee behind it, the
/// organisation, and the employee's own custom values. The renderer never fetches — it is
/// handed this and turns it into a page.
/// </summary>
public sealed class DocumentPrintContext
{
    public Document Document { get; set; } = new();
    public DocumentPrintEmployee Employee { get; set; } = new();
    public string? TenantName { get; set; }
    public List<Config.CustomValue> CustomValues { get; set; } = new();
}

/// <summary>
/// Counts across the whole register.
///
/// Every list endpoint here is paged and clamped, so a screen cannot total the register by
/// counting what it was sent — it would be right on page one and wrong on page two. These
/// are aggregates, and none of them grows with the number of documents.
/// </summary>
public sealed class DocumentStats
{
    public int TotalCount { get; set; }
    public int DraftCount { get; set; }
    public int PendingSignatureCount { get; set; }
    public int IssuedCount { get; set; }
    public int AcknowledgedCount { get; set; }
    public int ExpiredCount { get; set; }
    public int RevokedCount { get; set; }

    /// <summary>Everything past the preparation stages — out of the building.</summary>
    public int DeliveredCount { get; set; }

    /// <summary>Terms that have not lapsed and fall inside the next ninety days.</summary>
    public int Expiring90Count { get; set; }

    /// <summary>Documents carrying a valid-till date at all.</summary>
    public int DatedCount { get; set; }

    public DateTime? NextExpiry { get; set; }

    public List<DocumentTypeCount> ByType { get; set; } = new();
    public List<DocumentPeriodCount> ByMonth { get; set; } = new();
}

public sealed class DocumentTypeCount
{
    public string DocumentType { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
}

public sealed class DocumentPeriodCount
{
    /// <summary>yyyy-MM.</summary>
    public string Period { get; set; } = string.Empty;
    public int DocumentCount { get; set; }
}

/// <summary>The employee columns a printed letter is allowed to name.</summary>
public sealed class DocumentPrintEmployee
{
    public int EmployeeId { get; set; }
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
}
