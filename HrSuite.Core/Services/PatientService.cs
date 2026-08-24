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

        var validator = new Validator()
            .RequireText(patient.FullName, "Name is required.", "fullName")
            .RequireText(patient.Mobile, "Mobile is required.", "mobile");

        // A new registration may leave the UHID blank: the database issues the next one in
        // the tenant's series. An existing patient may not — a record that lost its
        // identifier would be a different patient to everything that refers to it.
        if (!isNew) validator = validator.RequireText(patient.PatientCode, "UHID is required.", "patientCode");

        var validation = validator.ToResult();
        if (validation.IsFailure) return Result<Patient>.Fail(validation.Errors.ToArray());

        patient.PatientCode = (patient.PatientCode ?? string.Empty).Trim();
        patient.FullName = patient.FullName.Trim();
        patient.Mobile = patient.Mobile.Trim();
        patient.Email = patient.Email?.Trim();
        patient.City = patient.City?.Trim();
        patient.Address = patient.Address?.Trim();

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
            switch (key.ToLowerInvariant())
            {
                case "fullname": patient.FullName = value?.ToString() ?? patient.FullName; break;
                case "gender": patient.Gender = value?.ToString(); break;
                case "mobile": patient.Mobile = value?.ToString() ?? patient.Mobile; break;
                case "email": patient.Email = value?.ToString(); break;
                case "bloodgroup": patient.BloodGroup = value?.ToString(); break;
                case "address": patient.Address = value?.ToString(); break;
                case "city": patient.City = value?.ToString(); break;
                case "dob": patient.Dob = AsDate(value); break;
                case "registeredon": patient.RegisteredOn = AsDate(value); break;
                default: break; // patientCode, patientId and anything unknown are ignored on purpose
            }
        }
    }

    private static DateTime? AsDate(object? value)
        => value switch
        {
            null => null,
            DateTime date => date,
            _ => DateTime.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
}
