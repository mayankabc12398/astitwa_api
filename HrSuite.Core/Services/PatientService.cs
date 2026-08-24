using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Extensibility;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 patient rules — true for every hospital, no customer's name anywhere.
///
///   * The UHID is unique per tenant.
///   * A registration carries a name and a phone number.
///
/// Anything narrower than that is deliberately absent. "Aadhaar is mandatory", "a minor
/// needs a guardian", "the UHID is P-yyyy-nnnn" are one hospital's rules, and they belong
/// in a cfg_field_rule row or a beforeSave script — not compiled in here where changing
/// them would need a deployment.
/// </summary>
public sealed class PatientService : IPatientService
{
    private readonly IPatientRepository _repository;
    private readonly HookInvoker _hooks;

    public PatientService(IPatientRepository repository, HookInvoker hooks)
    {
        _repository = repository;
        _hooks = hooks;
    }

    public Task<PagedResult<Patient>> ListAsync(PageRequest page, CancellationToken ct = default)
        => _repository.ListAsync(page, ct);

    public async Task<Result<Patient>> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        if (found is null) return Result<Patient>.NotFound("Patient not found.");

        await _hooks.RunAsync(HookKeys.PatientOnLoad, form: found, ct: ct).ConfigureAwait(false);
        return Result<Patient>.Success(found);
    }

    public async Task<Result<Patient>> SaveAsync(Patient patient, CancellationToken ct = default)
    {
        var isNew = patient.PatientId == 0;

        NormaliseNames(patient);

        // Keyed on the fields the screen actually renders. A rule against fullName would put
        // the error on a control nobody can see and leave the form looking correct.
        var validator = new Validator()
            .RequireText(patient.FirstName, "First name is required.", "firstName")
            .RequireText(patient.LastName, "Last name is required.", "lastName")
            .RequireText(patient.MobileNo, "Mobile number is required.", "mobileNo");

        // A new registration may leave the UHID blank: the database issues the next one in
        // the tenant's series. An existing patient may not — a record that lost its
        // identifier would be a different patient to everything that refers to it.
        if (!isNew) validator = validator.RequireText(patient.PatientCode, "UHID is required.", "patientCode");

        var validation = validator.ToResult();
        if (validation.IsFailure) return Result<Patient>.Fail(validation.Errors.ToArray());

        patient.PatientCode = (patient.PatientCode ?? string.Empty).Trim();
        patient.Email = patient.Email?.Trim();
        patient.City = patient.City?.Trim();
        patient.LocalAddress = patient.LocalAddress?.Trim();
        patient.PermanentAddress = patient.PermanentAddress?.Trim();

        // The permanent address is the local one when the desk ticked the box. Decided here
        // rather than trusted from the browser: the tick is what was meant, and two addresses
        // that disagree with it would be a record nobody can explain.
        if (patient.SameAsLocalAddress) patient.PermanentAddress = patient.LocalAddress;

        // Only a UHID somebody typed is checked here. A generated one is allocated inside
        // the save itself, under the row lock that hands it out, so checking it beforehand
        // would prove nothing about the moment it is used.
        if (patient.PatientCode.Length > 0
            && await _repository.CodeExistsAsync(patient.PatientCode, patient.PatientId, ct).ConfigureAwait(false))
        {
            return Result<Patient>.Fail(
                Error.Validation($"UHID '{patient.PatientCode}' is already in use.", "patientCode"));
        }

        // Layer 5 slot. With no script registered this returns an empty result and the save
        // proceeds exactly as written.
        var before = await _hooks.RunAsync(HookKeys.PatientBeforeSave, form: patient, ct: ct).ConfigureAwait(false);
        if (before.CancelSave)
        {
            return Result<Patient>.Invalid(before.Message ?? "The save was cancelled by a configured rule.");
        }

        ApplyScriptEdits(patient, before);

        var saved = await _repository.SaveAsync(patient, ct).ConfigureAwait(false);
        if (saved is null) return Result<Patient>.NotFound("Patient not found.");

        await _hooks.RunAsync(HookKeys.PatientAfterSave, form: patient, response: saved, ct: ct).ConfigureAwait(false);

        return Result<Patient>.Success(saved);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => _repository.LookupAsync(ct);

    /// <summary>
    /// A script may adjust the record through ctx.setForm(). Only fields a script is allowed
    /// to touch are copied back: the UHID is not among them, because a script that could
    /// rewrite an identifier could quietly merge two people's records.
    /// </summary>
    private static void ApplyScriptEdits(Patient patient, HookResult result)
    {
        if (result.Form is null || result.Form.Count == 0) return;

        foreach (var (key, value) in result.Form)
        {
            // patientCode, patientId and fullName are absent from the table on purpose: a
            // script that could rewrite an identifier could quietly merge two people's
            // records, and fullName is derived from the two name fields rather than set.
            if (Setters.TryGetValue(key, out var apply)) apply(patient, value);
        }
    }

    /// <summary>
    /// What a script may write back through ctx.setForm(), by field key.
    ///
    /// A table rather than a switch because there are sixty of them, and a switch that long
    /// buries the two rules that matter: the identifier fields are absent, and so is fullName.
    /// Matched case-insensitively, so a script may write mobileNo or mobileno.
    /// </summary>
    private static readonly Dictionary<string, Action<Patient, object?>> Setters =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["firstName"] = (p, v) => p.FirstName = v?.ToString() ?? p.FirstName,
            ["lastName"] = (p, v) => p.LastName = v?.ToString() ?? p.LastName,
            ["mobileNo"] = (p, v) => p.MobileNo = v?.ToString() ?? p.MobileNo,
            // The columns they replaced, so a script written against the old screen still runs.
            ["mobile"] = (p, v) => p.MobileNo = v?.ToString() ?? p.MobileNo,
            ["localAddress"] = (p, v) => p.LocalAddress = v?.ToString(),
            ["address"] = (p, v) => p.LocalAddress = v?.ToString(),
            ["dob"] = (p, v) => p.Dob = AsDate(v),
            ["registeredOn"] = (p, v) => p.RegisteredOn = AsDate(v),
            ["age"] = (p, v) => p.Age = AsInt(v),
            ["pregnancyDays"] = (p, v) => p.PregnancyDays = AsInt(v),
            ["sameAsLocalAddress"] = (p, v) => p.SameAsLocalAddress = AsBool(v),
            ["barcode"] = (p, v) => p.Barcode = v?.ToString(),
            ["title"] = (p, v) => p.Title = v?.ToString(),
            ["gender"] = (p, v) => p.Gender = v?.ToString(),
            ["maritalStatus"] = (p, v) => p.MaritalStatus = v?.ToString(),
            ["ageType"] = (p, v) => p.AgeType = v?.ToString(),
            ["email"] = (p, v) => p.Email = v?.ToString(),
            ["bloodGroup"] = (p, v) => p.BloodGroup = v?.ToString(),
            ["permanentAddress"] = (p, v) => p.PermanentAddress = v?.ToString(),
            ["city"] = (p, v) => p.City = v?.ToString(),
            ["country"] = (p, v) => p.Country = v?.ToString(),
            ["state"] = (p, v) => p.State = v?.ToString(),
            ["district"] = (p, v) => p.District = v?.ToString(),
            ["idProofName"] = (p, v) => p.IdProofName = v?.ToString(),
            ["idProofNo"] = (p, v) => p.IdProofNo = v?.ToString(),
            ["kraPin"] = (p, v) => p.KraPin = v?.ToString(),
            ["familyNumber"] = (p, v) => p.FamilyNumber = v?.ToString(),
            ["staffId"] = (p, v) => p.StaffId = v?.ToString(),
            ["dependentId"] = (p, v) => p.DependentId = v?.ToString(),
            ["nationalId"] = (p, v) => p.NationalId = v?.ToString(),
            ["altCountryCode"] = (p, v) => p.AltCountryCode = v?.ToString(),
            ["alternativeNo"] = (p, v) => p.AlternativeNo = v?.ToString(),
            ["occupation"] = (p, v) => p.Occupation = v?.ToString(),
            ["birthPlace"] = (p, v) => p.BirthPlace = v?.ToString(),
            ["religion"] = (p, v) => p.Religion = v?.ToString(),
            ["emgFirstName"] = (p, v) => p.EmgFirstName = v?.ToString(),
            ["emgLastName"] = (p, v) => p.EmgLastName = v?.ToString(),
            ["emgRelation"] = (p, v) => p.EmgRelation = v?.ToString(),
            ["emgMobileCode"] = (p, v) => p.EmgMobileCode = v?.ToString(),
            ["emgMobileNo"] = (p, v) => p.EmgMobileNo = v?.ToString(),
            ["emgResidentNo"] = (p, v) => p.EmgResidentNo = v?.ToString(),
            ["emgAddress"] = (p, v) => p.EmgAddress = v?.ToString(),
            ["isInternational"] = (p, v) => p.IsInternational = v?.ToString(),
            ["nationality"] = (p, v) => p.Nationality = v?.ToString(),
            ["passportNumber"] = (p, v) => p.PassportNumber = v?.ToString(),
            ["internationalNo"] = (p, v) => p.InternationalNo = v?.ToString(),
            ["locality"] = (p, v) => p.Locality = v?.ToString(),
            ["membershipNo"] = (p, v) => p.MembershipNo = v?.ToString(),
            ["patientType"] = (p, v) => p.PatientType = v?.ToString(),
            ["source"] = (p, v) => p.Source = v?.ToString(),
            ["empReferenceId"] = (p, v) => p.EmpReferenceId = v?.ToString(),
            ["identityMark"] = (p, v) => p.IdentityMark = v?.ToString(),
            ["identityMark2"] = (p, v) => p.IdentityMark2 = v?.ToString(),
            ["referenceType"] = (p, v) => p.ReferenceType = v?.ToString(),
            ["mlcType"] = (p, v) => p.MlcType = v?.ToString(),
            ["mlcNo"] = (p, v) => p.MlcNo = v?.ToString(),
            ["relationOf"] = (p, v) => p.RelationOf = v?.ToString(),
            ["relationName"] = (p, v) => p.RelationName = v?.ToString(),
            ["relationPhone"] = (p, v) => p.RelationPhone = v?.ToString(),
        };

    /// <summary>
    /// Fills the new name and contact fields from the ones they replaced, and keeps FullName,
    /// Mobile and Address in step with them.
    ///
    /// A client written against the first version of this screen still sends fullName, mobile
    /// and address, and it has to keep working — a desk on last week's build is not a reason
    /// to reject a patient. The database derives the same three columns again on the way in,
    /// so what is stored is right whichever way the record arrived.
    /// </summary>
    private static void NormaliseNames(Patient patient)
    {
        patient.FirstName = (patient.FirstName ?? string.Empty).Trim();
        patient.LastName = (patient.LastName ?? string.Empty).Trim();
        patient.MobileNo = (patient.MobileNo ?? string.Empty).Trim();

        if (patient.FirstName.Length == 0 && !string.IsNullOrWhiteSpace(patient.FullName))
        {
            var name = patient.FullName.Trim();
            var space = name.IndexOf(' ');
            patient.FirstName = space < 0 ? name : name[..space];
            if (space >= 0) patient.LastName = name[(space + 1)..].Trim();
        }

        if (patient.MobileNo.Length == 0 && !string.IsNullOrWhiteSpace(patient.Mobile))
        {
            patient.MobileNo = patient.Mobile.Trim();
        }

        if (string.IsNullOrWhiteSpace(patient.LocalAddress) && !string.IsNullOrWhiteSpace(patient.Address))
        {
            patient.LocalAddress = patient.Address.Trim();
        }

        patient.FullName = $"{patient.FirstName} {patient.LastName}".Trim();
        patient.Mobile = patient.MobileNo;
        patient.Address = patient.LocalAddress;
    }

    private static int? AsInt(object? value)
        => value switch
        {
            null => null,
            int number => number,
            _ => int.TryParse(value.ToString(), out var parsed) ? parsed : null
        };

    private static bool AsBool(object? value)
        => value switch
        {
            null => false,
            bool flag => flag,
            _ => value.ToString() is "1" or "true" or "True" or "yes" or "Y"
        };

    private static DateTime? AsDate(object? value)
        => value switch
        {
            null => null,
            DateTime date => date,
            _ => DateTime.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
}
