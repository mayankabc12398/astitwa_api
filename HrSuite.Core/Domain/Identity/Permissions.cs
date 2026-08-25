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
    public const string PatientView      = "hr.patient.view";
    public const string PatientEdit      = "hr.patient.edit";
    public const string EmployeeView     = "hr.employee.view";
    public const string EmployeeEdit     = "hr.employee.edit";
    public const string LeaveView        = "hr.leave.view";
    public const string LeaveEdit        = "hr.leave.edit";
    public const string LeaveApprove     = "hr.leave.approve";
    public const string DocumentView     = "hr.document.view";
    public const string DocumentEdit     = "hr.document.edit";
    /// <summary>
    /// Separate from edit on purpose: preparing a draft and putting a letter in somebody's
    /// hand are different acts, and many tenants hold the second one more tightly.
    /// </summary>
    public const string DocumentIssue    = "hr.document.issue";

    /// <summary>Gates the Layer 5 admin screens: script hooks, named queries, hook log.</summary>
    public const string AdminExtensions  = "admin.extensions";
    /// <summary>Gates module and integration switches.</summary>
    public const string AdminTenant      = "admin.tenant";
    /// <summary>Gates the print designer. Reading a template is not gated by it.</summary>
    public const string AdminPrintTemplate = "admin.printTemplate";
    /// <summary>Gates the field builder. Reading the definitions is not gated by it.</summary>
    public const string AdminCustomField   = "admin.customField";
    /// <summary>The builder that adds real columns. Separate from AdminCustomField: one
    /// writes rows, the other changes the schema, and a tenant may reasonably allow the
    /// first without the second.</summary>
    public const string AdminFieldColumn   = "admin.fieldColumn";
    public const string JobRequisitionView = "hr.jobRequisition.view";
    public const string JobRequisitionEdit = "hr.jobRequisition.edit";

    public static readonly IReadOnlyList<string> All = new[]
    {
        DepartmentView, DepartmentEdit,
        DesignationView, DesignationEdit,
        PatientView, PatientEdit,
        EmployeeView, EmployeeEdit,
        LeaveView, LeaveEdit, LeaveApprove,
        DocumentView, DocumentEdit, DocumentIssue,
        AdminExtensions, AdminTenant, AdminPrintTemplate, AdminCustomField
    };
}
