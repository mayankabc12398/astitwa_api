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

    /// <summary>
    /// One compiled field on a screen.
    ///
    /// Section and Seq exist for the Field Builder: a tenant adding a field has to be able to
    /// say WHERE it goes, and "after Mobile Number, in Personal Details" is only answerable if
    /// the compiled fields carry their own section and position. Seq is the same number the
    /// screen passes to DynamicField as defaultSeq, so a custom field slotted between two of
    /// them renders exactly where the author put it.
    ///
    /// Both are optional: a screen that has never been sectioned leaves them at their
    /// defaults and the builder falls back to appending.
    /// </summary>
    public sealed record ScreenField(string Key, string Label, string? Section = null, int Seq = 0);

    /// <summary>A band on the form — the card its fields are drawn inside.</summary>
    public sealed record ScreenSection(string Key, string Label);

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
        IReadOnlyList<ScreenField> Fields,
        IReadOnlyList<ScreenSection>? Sections = null);

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
            // Every field the registration screen renders, so a script can be hung on any of
            // them without its hook key being typed by hand, and so the Field Builder can offer
            // "after this field, in that section" instead of a sequence number to guess at.
            //
            // fullName, mobile and address are absent although the columns still exist. They
            // are derived by the save now, and offering a slot on a field the user cannot see
            // would be advertising a hook that fires on nobody's keystroke.
            new ScreenField[]
            {
                new("barcode",            "Barcode",               "personal", 10),
                new("mobileNo",           "Mobile Number",         "personal", 20),
                new("title",              "Title",                 "personal", 30),
                new("firstName",          "First Name",            "personal", 40),
                new("lastName",           "Last Name",             "personal", 50),
                new("gender",             "Gender",                "personal", 60),
                new("maritalStatus",      "Marital Status",        "personal", 70),
                new("dob",                "DOB",                   "personal", 80),
                new("age",                "Age",                   "personal", 90),
                new("ageType",            "Type",                  "personal", 100),
                new("email",              "EMAIL",                 "personal", 110),
                new("localAddress",       "Local Address",         "personal", 120),
                new("sameAsLocalAddress", "Same as local address", "personal", 130),
                new("permanentAddress",   "PERMANENT ADDRESS",     "personal", 140),
                new("country",            "Country",               "personal", 150),
                new("state",              "State",                 "personal", 160),
                new("district",           "District",              "personal", 170),
                new("city",               "City",                  "personal", 180),
                new("idProofName",        "Id Proof Name",         "personal", 190),
                new("idProofNo",          "Id Proof No",           "personal", 200),
                new("kraPin",             "KRA PIN",               "personal", 210),
                new("familyNumber",       "Family Number",         "personal", 220),
                new("staffId",            "STAFF ID",              "personal", 230),
                new("dependentId",        "Dependent ID",          "personal", 240),
                new("nationalId",         "National ID",           "personal", 250),
                new("pregnancyDays",      "PREGNANCY DAYS",        "personal", 260),
                new("altCountryCode",     "Code",                  "other", 300),
                new("alternativeNo",      "Alternative No",        "other", 310),
                new("occupation",         "Occupation",            "other", 320),
                new("birthPlace",         "Birth Place",           "other", 330),
                new("religion",           "Religion",              "other", 340),
                new("emgFirstName",       "Emg First Name",        "other", 350),
                new("emgLastName",        "Emg Last Name",         "other", 360),
                new("emgRelation",        "Emg Relation",          "other", 370),
                new("emgMobileCode",      "Emg Code",              "other", 380),
                new("emgMobileNo",        "Emg Mobile No",         "other", 390),
                new("emgResidentNo",      "Emg Resident No",       "other", 400),
                new("emgAddress",         "Emg Address",           "other", 410),
                new("isInternational",    "Is International",      "other", 420),
                new("nationality",        "Nationality",           "other", 430),
                new("passportNumber",     "Passport Number",       "other", 440),
                new("internationalNo",    "International No",      "other", 450),
                new("locality",           "Locality",              "other", 460),
                new("membershipNo",       "Membership No",         "other", 470),
                new("patientType",        "Patient Type",          "other", 480),
                new("source",             "Source",                "other", 490),
                new("empReferenceId",     "Emp Reference Id",      "other", 500),
                new("identityMark",       "Identity Mark",         "other", 510),
                new("identityMark2",      "Identity Mark 2",       "other", 520),
                new("referenceType",      "Reference Type",        "other", 530),
                new("mlcType",            "Mlc Type",              "other", 540),
                new("mlcNo",              "Mlc No",                "other", 550),
                new("relationOf",         "Relation Of",           "other", 560),
                new("relationName",       "Relation Name",         "other", 570),
                new("relationPhone",      "Relation Phone",        "other", 580),
                new("insuranceGroup",     "Insurance Group",       "scheme", 600),
                new("insurance",          "Insurance",             "scheme", 610),
                new("panel",              "Panel",                 "scheme", 620),
                new("policyNo",           "Policy No",             "scheme", 630),
                new("policyCardNo",       "Policy Card No",        "scheme", 640),
                new("nameOnCard",         "Name On Card",          "scheme", 650),
                new("expireDate",         "Expire Date",           "scheme", 660),
                new("cardHolder",         "Card Holder",           "scheme", 670),
                new("approvalAmount",     "Approval Amount",       "scheme", 680),
                new("approvalRemark",     "Approval Remark",       "scheme", 690),
                new("patientCode",        "UHID",                  "personal", 5)
            },
            new ScreenSection[]
            {
                new("personal", "Personal Details"),
                new("other",    "Other Details"),
                new("scheme",   "Scheme Details")
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
