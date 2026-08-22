namespace HrSuite.Core.Extensibility;

/// <summary>
/// The hook slots compiled into the base screens. Adding a slot is a Layer 1 change;
/// filling one is a Layer 5 change (a database row).
///
/// Every form screen carries the same three screen-level slots and the same per-field events,
/// because they all run through one useScreenHooks on the client. A slot listed here that no
/// screen fires would be worse than no slot at all: it would save, activate, log nothing, and
/// leave its author unable to tell a broken script from an unused one.
/// </summary>
public static class HookKeys
{
    public const string EmployeeOnLoad         = "hr.employee.onLoad";
    public const string EmployeeBeforeSave     = "hr.employee.beforeSave";
    public const string EmployeeAfterSave      = "hr.employee.afterSave";

    public const string LeaveRequestOnLoad     = "hr.leaveRequest.onLoad";
    public const string LeaveRequestBeforeSave = "hr.leaveRequest.beforeSave";
    public const string LeaveRequestAfterSave  = "hr.leaveRequest.afterSave";

    public const string DepartmentOnLoad       = "hr.department.onLoad";
    public const string DepartmentBeforeSave   = "hr.department.beforeSave";
    public const string DepartmentAfterSave    = "hr.department.afterSave";

    public const string DesignationOnLoad      = "hr.designation.onLoad";
    public const string DesignationBeforeSave  = "hr.designation.beforeSave";
    public const string DesignationAfterSave   = "hr.designation.afterSave";

    /// <summary>hr.employee.field.&lt;fieldKey&gt;.onBlur</summary>
    public static string EmployeeFieldOnBlur(string fieldKey) => $"hr.employee.field.{fieldKey}.onBlur";

    /// <summary>
    /// The flat list the editor falls back to when no page is picked. The &lt;fieldKey&gt;
    /// entries describe a shape rather than being keys in their own right — with a page
    /// chosen, the editor offers real field slots from <see cref="ScreenCatalog"/> instead.
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        EmployeeOnLoad, EmployeeBeforeSave, EmployeeAfterSave,
        LeaveRequestOnLoad, LeaveRequestBeforeSave, LeaveRequestAfterSave,
        DepartmentOnLoad, DepartmentBeforeSave, DepartmentAfterSave,
        DesignationOnLoad, DesignationBeforeSave, DesignationAfterSave,
        "hr.<screen>.field.<fieldKey>.onBlur",
        "hr.<screen>.field.<fieldKey>.onChange"
    };
}
