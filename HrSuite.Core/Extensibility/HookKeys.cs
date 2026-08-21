namespace HrSuite.Core.Extensibility;

/// <summary>
/// The hook slots compiled into the base screens. Adding a slot is a Layer 1 change;
/// filling one is a Layer 5 change (a database row).
/// </summary>
public static class HookKeys
{
    public const string EmployeeOnLoad        = "hr.employee.onLoad";
    public const string EmployeeBeforeSave    = "hr.employee.beforeSave";
    public const string EmployeeAfterSave     = "hr.employee.afterSave";
    public const string LeaveRequestBeforeSave = "hr.leaveRequest.beforeSave";
    public const string LeaveRequestAfterSave  = "hr.leaveRequest.afterSave";

    /// <summary>hr.employee.field.&lt;fieldKey&gt;.onBlur</summary>
    public static string EmployeeFieldOnBlur(string fieldKey) => $"hr.employee.field.{fieldKey}.onBlur";

    public static readonly IReadOnlyList<string> All = new[]
    {
        EmployeeOnLoad, EmployeeBeforeSave, EmployeeAfterSave,
        "hr.employee.field.<fieldKey>.onBlur",
        LeaveRequestBeforeSave, LeaveRequestAfterSave
    };
}
