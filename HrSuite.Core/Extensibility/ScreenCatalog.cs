namespace HrSuite.Core.Extensibility;

/// <summary>
/// What the hook editor offers: the screens base code compiled slots into, the slots on each
/// one, and the fields each screen actually has.
///
/// Why the field names live here rather than being read from configuration: cfg_field_rule
/// only holds the fields a tenant has OVERRIDDEN, so it is a subset and would offer an
/// author two fields on a screen that has ten. The full list is a fact about the compiled
/// screen — the same kind of fact as <see cref="HookKeys"/> — so it is declared in the same
/// place, and adding a field to a screen means adding it here too.
///
/// The catalogue is a convenience for the editor, not a rule. The hook key remains free
/// text: a key that is not in this list still saves and still runs, which is what keeps a
/// screen shipped tomorrow from being locked out of hooks today.
/// </summary>
public static class ScreenCatalog
{
    public sealed record HookSlot(string Key, string Label);

    public sealed record ScreenField(string Key, string Label);

    public sealed record Screen(
        string Key,
        string Label,
        IReadOnlyList<HookSlot> Slots,
        IReadOnlyList<ScreenField> Fields);

    /// <summary>Appended to a field key to form the per-field slot on a screen.</summary>
    public const string FieldSlotSuffix = "onBlur";

    public static readonly IReadOnlyList<Screen> Screens = new[]
    {
        new Screen(
            "hr.employee",
            "Employee",
            new HookSlot[]
            {
                new(HookKeys.EmployeeOnLoad,     "On load"),
                new(HookKeys.EmployeeBeforeSave, "Before save"),
                new(HookKeys.EmployeeAfterSave,  "After save")
            },
            new ScreenField[]
            {
                new("employeeCode",       "Employee code"),
                new("fullName",           "Name"),
                new("dob",                "Date of birth"),
                new("dateOfJoining",      "Date of joining"),
                new("departmentId",       "Department"),
                new("designationId",      "Designation"),
                new("reportingManagerId", "Reporting manager"),
                new("mobile",             "Mobile"),
                new("email",              "Email"),
                new("employmentStatus",   "Employment status"),
                new("grossCtc",           "Gross CTC"),
                new("hra",                "HRA"),
                new("tds",                "TDS"),
                new("netSalary",          "Net salary")
            }),

        new Screen(
            "hr.leaveRequest",
            "Leave request",
            new HookSlot[]
            {
                new(HookKeys.LeaveRequestBeforeSave, "Before save"),
                new(HookKeys.LeaveRequestAfterSave,  "After save")
            },
            // The leave screen has no per-field slot compiled in, so it offers no fields.
            // Listing them here would advertise hooks that never fire.
            Array.Empty<ScreenField>())
    };

    /// <summary>hr.employee.field.grossCtc.onBlur</summary>
    public static string FieldSlotKey(string screenKey, string fieldKey)
        => $"{screenKey}.field.{fieldKey}.{FieldSlotSuffix}";
}
