namespace HrSuite.Core.Domain.Hr;

/// <summary>A code/label pair for dropdowns. Bounded by the procedure, never unbounded.</summary>
public sealed class LookupItem
{
    public int Id { get; set; }
    public string Label { get; set; } = string.Empty;
}

// ---------------------------------------------------------------------
// Department
// ---------------------------------------------------------------------

public sealed class Department
{
    public int DepartmentId { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

// ---------------------------------------------------------------------
// Designation
// ---------------------------------------------------------------------

public sealed class Designation
{
    public int DesignationId { get; set; }
    public string DesigCode { get; set; } = string.Empty;
    public string DesigName { get; set; } = string.Empty;
    public string? Grade { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? CreatedOn { get; set; }
    public DateTime? UpdatedOn { get; set; }
}

// ---------------------------------------------------------------------
// Patient
// ---------------------------------------------------------------------

/// <summary>
/// A registered patient: the front-desk record everything downstream hangs off, not a chart.
///
/// One class serves both the grid and the form, unlike Employee, because nothing here is
/// resolved from another table — a list row has no foreign name to look up. The list
/// procedure fills the handful of columns the grid shows and leaves the rest null; the form
/// reads one patient at a time and gets all of it.
///
/// FullName, Mobile and Address are the columns the first registration screen wrote. They are
/// kept because print templates, named queries and every downstream module read them, but
/// nothing sends them any more: the save derives FullName from FirstName + LastName and
/// mirrors Mobile and Address from MobileNo and LocalAddress.
/// </summary>
public sealed class Patient
{
    public int PatientId { get; set; }
    /// <summary>The UHID. Unique inside the tenant; the index is what says so.</summary>
    public string PatientCode { get; set; } = string.Empty;
    public string? Barcode { get; set; }

    // ----- name -----
    /// <summary>Derived from FirstName + LastName by the save. Read-only in practice.</summary>
    public string FullName { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Title { get; set; }

    public string? Gender { get; set; }
    public string? MaritalStatus { get; set; }
    public DateTime? Dob { get; set; }
    /// <summary>Age in <see cref="AgeType"/> units — a newborn is registered in days.</summary>
    public int? Age { get; set; }
    /// <summary>YRS, MTH or DAYS.</summary>
    public string? AgeType { get; set; }

    /// <summary>Required: it is how a desk finds a returning patient.</summary>
    public string MobileNo { get; set; } = string.Empty;
    /// <summary>The column MobileNo replaced. Mirrored by the save.</summary>
    public string Mobile { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? BloodGroup { get; set; }

    // ----- address -----
    public string? LocalAddress { get; set; }
    /// <summary>The column LocalAddress replaced. Mirrored by the save.</summary>
    public string? Address { get; set; }
    public bool SameAsLocalAddress { get; set; }
    public string? PermanentAddress { get; set; }
    public string? City { get; set; }
    public string? Country { get; set; }
    public string? State { get; set; }
    public string? District { get; set; }

    // ----- identifiers -----
    public string? IdProofName { get; set; }
    public string? IdProofNo { get; set; }
    public string? KraPin { get; set; }
    public string? FamilyNumber { get; set; }
    public string? StaffId { get; set; }
    public string? DependentId { get; set; }
    public string? NationalId { get; set; }
    public int? PregnancyDays { get; set; }

    // ----- other details -----
    public string? AltCountryCode { get; set; }
    public string? AlternativeNo { get; set; }
    public string? Occupation { get; set; }
    public string? BirthPlace { get; set; }
    public string? Religion { get; set; }
    public string? EmgFirstName { get; set; }
    public string? EmgLastName { get; set; }
    public string? EmgRelation { get; set; }
    public string? EmgMobileCode { get; set; }
    public string? EmgMobileNo { get; set; }
    public string? EmgResidentNo { get; set; }
    public string? EmgAddress { get; set; }
    public string? IsInternational { get; set; }
    public string? Nationality { get; set; }
    public string? PassportNumber { get; set; }
    public string? InternationalNo { get; set; }
    public string? Locality { get; set; }
    public string? MembershipNo { get; set; }
    public string? PatientType { get; set; }
    public string? Source { get; set; }
    public string? EmpReferenceId { get; set; }
    public string? IdentityMark { get; set; }
    public string? IdentityMark2 { get; set; }
    public string? ReferenceType { get; set; }
    public string? MlcType { get; set; }
    public string? MlcNo { get; set; }
    public string? RelationOf { get; set; }
    public string? RelationName { get; set; }
    public string? RelationPhone { get; set; }

    public DateTime? RegisteredOn { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The policies this patient is covered by.
    ///
    /// Null and empty are different on the way in: null is "not editing schemes" and leaves
    /// the stored set alone, empty is "there are none" and clears it. The form always sends a
    /// list, so it always means what it says.
    /// </summary>
    public List<PatientScheme>? Schemes { get; set; }
}

/// <summary>
/// One insurance policy on a patient. Owned by the patient — the save replaces the whole set,
/// so SchemeId is not stable across a save and nothing refers to it.
/// </summary>
public sealed class PatientScheme
{
    public int SchemeId { get; set; }
    public int PatientId { get; set; }
    public int SeqNo { get; set; }
    public string? InsuranceGroup { get; set; }
    public string? Insurance { get; set; }
    public string? Panel { get; set; }
    public string? PolicyNo { get; set; }
    public string? PolicyCardNo { get; set; }
    public string? NameOnCard { get; set; }
    public DateTime? ExpireDate { get; set; }
    public string? CardHolder { get; set; }
    public decimal? ApprovalAmount { get; set; }
    public string? ApprovalRemark { get; set; }
}

// ---------------------------------------------------------------------
// Employee
// ---------------------------------------------------------------------

public sealed class Employee
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public DateTime? Dob { get; set; }
    public DateTime? DateOfJoining { get; set; }
    public int? DepartmentId { get; set; }
    public int? DesignationId { get; set; }
    public int? ReportingManagerId { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string EmploymentStatus { get; set; } = "Active";

    // Compensation. decimal, not double: money must not carry binary rounding error.
    // All nullable — a tenant that does not track pay here leaves them empty, and
    // cfg_field_rule hides the inputs rather than the product pretending they are zero.
    public decimal? GrossCtc { get; set; }
    public decimal? Hra { get; set; }
    public decimal? Tds { get; set; }
    public decimal? NetSalary { get; set; }

    public bool IsActive { get; set; } = true;
}

/// <summary>List-row projection. Carries the resolved names so the grid needs no second call.</summary>
public sealed class EmployeeListItem
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public string EmploymentStatus { get; set; } = string.Empty;
    public DateTime? DateOfJoining { get; set; }
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int? DesignationId { get; set; }
    public string? DesignationName { get; set; }
    public int? ReportingManagerId { get; set; }
    public string? ReportingManagerName { get; set; }
}

// ---------------------------------------------------------------------
// Leave
// ---------------------------------------------------------------------

public static class LeaveStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}

public sealed class LeaveRequest
{
    public int LeaveRequestId { get; set; }
    public int EmployeeId { get; set; }
    public int LeaveTypeId { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal Days { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = LeaveStatus.Pending;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedOn { get; set; }
    public string? ApprovalRemark { get; set; }
}

public sealed class LeaveRequestListItem
{
    public int LeaveRequestId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int LeaveTypeId { get; set; }
    public string? LeaveTypeName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal Days { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedOn { get; set; }
    public string? ApprovalRemark { get; set; }
}

/// <summary>The shape returned after a save or a decision, with names resolved for notification tokens.</summary>
public sealed class LeaveRequestDetail
{
    public int LeaveRequestId { get; set; }
    public int EmployeeId { get; set; }
    public string EmployeeName { get; set; } = string.Empty;
    public string? EmployeeEmail { get; set; }
    public int LeaveTypeId { get; set; }
    public string? LeaveTypeName { get; set; }
    public DateTime FromDate { get; set; }
    public DateTime ToDate { get; set; }
    public decimal Days { get; set; }
    public string? Reason { get; set; }
    public string Status { get; set; } = string.Empty;
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedOn { get; set; }
    public string? ApprovalRemark { get; set; }
}

public sealed class LeaveDecision
{
    public int LeaveRequestId { get; set; }
    public bool Approve { get; set; }
    public string? Remark { get; set; }
}

// ---------------------------------------------------------------------
// Job requisition
// ---------------------------------------------------------------------

/// <summary>
/// A vacancy being raised, captured over three steps: the role, then the money and dates,
/// then a review of both.
///
/// The first screen whose extra fields are real columns rather than rows — anything a
/// hospital adds through the Screen Field Builder lands on hr_job_requisition beside these
/// and travels in <see cref="Extra"/>.
/// </summary>
public sealed class JobRequisition
{
    public int RequisitionId { get; set; }
    public string RequisitionCode { get; set; } = string.Empty;

    // Step 1 — the role
    public string JobTitle { get; set; } = string.Empty;
    public int? DepartmentId { get; set; }
    public string? DepartmentName { get; set; }
    public int Openings { get; set; } = 1;
    public string? ExperienceRange { get; set; }
    public string? EmploymentType { get; set; }
    public string? Priority { get; set; }
    public string? KeySkills { get; set; }

    // Step 2 — money and dates
    public decimal? BudgetMin { get; set; }
    public decimal? BudgetMax { get; set; }
    public DateTime? TargetDate { get; set; }
    public string? Notes { get; set; }

    public string Status { get; set; } = "Draft";
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Values for the cf_ columns the Screen Field Builder added, keyed by column name.
    ///
    /// Kept as a bag rather than typed properties because the whole point of the feature is
    /// that the product does not know what a hospital added. The repository writes only the
    /// keys that are live columns on the table, so a stale key from an old browser tab is
    /// ignored rather than failing the save.
    /// </summary>
    public Dictionary<string, object?> Extra { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
