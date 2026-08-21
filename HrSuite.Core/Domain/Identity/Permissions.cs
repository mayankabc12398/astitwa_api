namespace HrSuite.Core.Domain.Identity;

/// <summary>
/// The permission vocabulary base code understands. Roles are per tenant rows; these keys are not.
/// An add-on declares its own keys in its own assembly.
/// </summary>
public static class Permissions
{
    public const string DepartmentView   = "hr.department.view";
    public const string DepartmentEdit   = "hr.department.edit";
    public const string DesignationView  = "hr.designation.view";
    public const string DesignationEdit  = "hr.designation.edit";
    public const string EmployeeView     = "hr.employee.view";
    public const string EmployeeEdit     = "hr.employee.edit";
    public const string LeaveView        = "hr.leave.view";
    public const string LeaveEdit        = "hr.leave.edit";
    public const string LeaveApprove     = "hr.leave.approve";

    /// <summary>Gates the Layer 5 admin screens: script hooks, named queries, hook log.</summary>
    public const string AdminExtensions  = "admin.extensions";
    /// <summary>Gates module and integration switches.</summary>
    public const string AdminTenant      = "admin.tenant";

    public static readonly IReadOnlyList<string> All = new[]
    {
        DepartmentView, DepartmentEdit,
        DesignationView, DesignationEdit,
        EmployeeView, EmployeeEdit,
        LeaveView, LeaveEdit, LeaveApprove,
        AdminExtensions, AdminTenant
    };
}
