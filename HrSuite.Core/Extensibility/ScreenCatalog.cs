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

    public sealed record FieldEvent(string Key, string Label);

    /// <summary>
    /// The events a field slot can be bound to. A screen fires both: onBlur when the control
    /// is left, onChange as the value is typed — the second is what debounce_ms is for.
    ///
    /// Adding an entry here advertises a slot to the editor, so nothing goes in this list
    /// until a screen actually fires it. A hook nobody fires looks identical to a broken one.
    /// </summary>
    public static readonly IReadOnlyList<FieldEvent> FieldEvents = new FieldEvent[]
    {
        new("onBlur",   "On blur"),
        new("onChange", "On change")
    };

    public sealed record Screen(
        string Key,
        string Label,
        IReadOnlyList<HookSlot> Slots,
        IReadOnlyList<ScreenField> Fields);

    /// <summary>
    /// The event assumed when a caller names none. onBlur rather than onChange because it is
    /// the older of the two: a key built without an event has to keep meaning what it meant.
    /// </summary>
    public const string FieldSlotSuffix = "onBlur";

    public static readonly IReadOnlyList<Screen> Screens = new[]
    {
        new Screen(
            "hr.patient",
            "Patient",
            new HookSlot[]
            {
                new(HookKeys.PatientOnLoad,     "On load"),
                new(HookKeys.PatientBeforeSave, "Before save"),
                new(HookKeys.PatientAfterSave,  "After save")
            },
            new ScreenField[]
            {
                new("patientCode",  "UHID"),
                new("fullName",     "Name"),
                new("gender",       "Gender"),
                new("dob",          "Date of birth"),
                new("mobile",       "Mobile"),
                new("email",        "Email"),
                new("bloodGroup",   "Blood group"),
                new("address",      "Address"),
                new("city",         "City"),
                new("registeredOn", "Registered on")
            }),

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
                new(HookKeys.LeaveRequestOnLoad,     "On load"),
                new(HookKeys.LeaveRequestBeforeSave, "Before save"),
                new(HookKeys.LeaveRequestAfterSave,  "After save")
            },
            new ScreenField[]
            {
                new("employeeId",  "Employee"),
                new("leaveTypeId", "Leave type"),
                new("fromDate",    "From"),
                new("toDate",      "To"),
                new("reason",      "Reason")
            }),

        new Screen(
            "hr.department",
            "Department",
            new HookSlot[]
            {
                new(HookKeys.DepartmentOnLoad,     "On load"),
                new(HookKeys.DepartmentBeforeSave, "Before save"),
                new(HookKeys.DepartmentAfterSave,  "After save")
            },
            new ScreenField[]
            {
                new("deptCode", "Code"),
                new("deptName", "Name")
            }),

        new Screen(
            "hr.designation",
            "Designation",
            new HookSlot[]
            {
                new(HookKeys.DesignationOnLoad,     "On load"),
                new(HookKeys.DesignationBeforeSave, "Before save"),
                new(HookKeys.DesignationAfterSave,  "After save")
            },
            new ScreenField[]
            {
                new("desigCode", "Code"),
                new("desigName", "Name"),
                new("grade",     "Grade")
            })
    };

    /// <summary>hr.employee.field.grossCtc — the slot without its event.</summary>
    public static string FieldSlotBase(string screenKey, string fieldKey)
        => $"{screenKey}.field.{fieldKey}";

    /// <summary>hr.employee.field.grossCtc.onBlur</summary>
    public static string FieldSlotKey(string screenKey, string fieldKey, string? eventKey = null)
        => $"{FieldSlotBase(screenKey, fieldKey)}.{eventKey ?? FieldSlotSuffix}";
}
