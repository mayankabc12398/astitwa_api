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
