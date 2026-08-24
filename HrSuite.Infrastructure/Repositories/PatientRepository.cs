using System.Text.Json;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class PatientRepository : RepositoryBase, IPatientRepository
{
    public PatientRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<Patient>> ListAsync(PageRequest page, CancellationToken ct = default)
        => QueryPagedAsync<Patient>("sp_hr_patient_list", page, ct: ct);

    /// <summary>
    /// The patient and their schemes, in one round trip. Two calls would let a concurrent
    /// save hand the caller a patient and a set of policies that never coexisted.
    /// </summary>
    public async Task<Patient?> GetAsync(int patientId, CancellationToken ct = default)
    {
        var (patients, schemes) = await QueryTwoAsync<Patient, PatientScheme>(
            "sp_hr_patient_get",
            ProcArgs.New().Set("patient_id", patientId),
            ct).ConfigureAwait(false);

        var patient = patients.FirstOrDefault();
        if (patient is null) return null;

        patient.Schemes = schemes.ToList();
        return patient;
    }

    public async Task<Patient?> SaveAsync(Patient patient, CancellationToken ct = default)
    {
        var saved = await ExecuteReturningAsync<Patient>(
            "sp_hr_patient_save",
            ProcArgs.New()
                .Set("patient_id", patient.PatientId)
                // Blank means "issue one": the procedure reads NULL and the empty
                // string the same way, but NULL is what the intent looks like.
                .Set("patient_code", string.IsNullOrWhiteSpace(patient.PatientCode) ? null : patient.PatientCode)
                .Set("barcode", patient.Barcode)
                .Set("first_name", patient.FirstName)
                .Set("last_name", patient.LastName)
                .Set("title", patient.Title)
                .Set("gender", patient.Gender)
                .Set("marital_status", patient.MaritalStatus)
                .Set("dob", patient.Dob)
                .Set("age", patient.Age)
                .Set("age_type", patient.AgeType)
                .Set("mobile_no", patient.MobileNo)
                .Set("email", patient.Email)
                .Set("blood_group", patient.BloodGroup)
                .Set("local_address", patient.LocalAddress)
                .Set("same_as_local_address", patient.SameAsLocalAddress ? 1 : 0)
                .Set("permanent_address", patient.PermanentAddress)
                .Set("city", patient.City)
                .Set("country", patient.Country)
                .Set("state", patient.State)
                .Set("district", patient.District)
                .Set("id_proof_name", patient.IdProofName)
                .Set("id_proof_no", patient.IdProofNo)
                .Set("kra_pin", patient.KraPin)
                .Set("family_number", patient.FamilyNumber)
                .Set("staff_id", patient.StaffId)
                .Set("dependent_id", patient.DependentId)
                .Set("national_id", patient.NationalId)
                .Set("pregnancy_days", patient.PregnancyDays)
                .Set("alt_country_code", patient.AltCountryCode)
                .Set("alternative_no", patient.AlternativeNo)
                .Set("occupation", patient.Occupation)
                .Set("birth_place", patient.BirthPlace)
                .Set("religion", patient.Religion)
                .Set("emg_first_name", patient.EmgFirstName)
                .Set("emg_last_name", patient.EmgLastName)
                .Set("emg_relation", patient.EmgRelation)
                .Set("emg_mobile_code", patient.EmgMobileCode)
                .Set("emg_mobile_no", patient.EmgMobileNo)
                .Set("emg_resident_no", patient.EmgResidentNo)
                .Set("emg_address", patient.EmgAddress)
                .Set("is_international", patient.IsInternational)
                .Set("nationality", patient.Nationality)
                .Set("passport_number", patient.PassportNumber)
                .Set("international_no", patient.InternationalNo)
                .Set("locality", patient.Locality)
                .Set("membership_no", patient.MembershipNo)
                .Set("patient_type", patient.PatientType)
                .Set("source", patient.Source)
                .Set("emp_reference_id", patient.EmpReferenceId)
                .Set("identity_mark", patient.IdentityMark)
                .Set("identity_mark_2", patient.IdentityMark2)
                .Set("reference_type", patient.ReferenceType)
                .Set("mlc_type", patient.MlcType)
                .Set("mlc_no", patient.MlcNo)
                .Set("relation_of", patient.RelationOf)
                .Set("relation_name", patient.RelationName)
                .Set("relation_phone", patient.RelationPhone)
                .Set("registered_on", patient.RegisteredOn)
                .Set("schemes", SchemesJson(patient.Schemes)),
            ct).ConfigureAwait(false);

        if (saved is null) return null;

        // Re-read: the save returns the patient row, and the caller wants the schemes the
        // procedure has just rewritten alongside it.
        return await GetAsync(saved.PatientId, ct).ConfigureAwait(false) ?? saved;
    }

    public Task DeleteAsync(int patientId, CancellationToken ct = default)
        => ExecuteAsync("sp_hr_patient_delete", ProcArgs.New().Set("patient_id", patientId), ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => QueryAsync<LookupItem>("sp_hr_patient_lookup", ct: ct);

    public async Task<bool> CodeExistsAsync(string patientCode, int patientId, CancellationToken ct = default)
    {
        var count = await ScalarAsync<int?>(
            "sp_hr_patient_code_exists",
            ProcArgs.New()
                .Set("patient_code", patientCode)
                .Set("patient_id", patientId),
            ct).ConfigureAwait(false);

        return (count ?? 0) > 0;
    }

    /// <summary>
    /// The scheme rows as the JSON array sp_hr_patient_save reads with JSON_TABLE.
    ///
    /// Written out field by field rather than serialised straight from the model: the date has
    /// to arrive as yyyy-MM-dd for JSON_TABLE's DATE path to read, and a serialised DateTime
    /// carries a time component the column cannot hold.
    ///
    /// null in, null out — the procedure leaves the stored set alone when it is given NULL.
    /// </summary>
    private static string? SchemesJson(IReadOnlyList<PatientScheme>? schemes)
    {
        if (schemes is null) return null;

        var rows = schemes.Select(scheme => new
        {
            insuranceGroup = scheme.InsuranceGroup,
            insurance = scheme.Insurance,
            panel = scheme.Panel,
            policyNo = scheme.PolicyNo,
            policyCardNo = scheme.PolicyCardNo,
            nameOnCard = scheme.NameOnCard,
            expireDate = scheme.ExpireDate?.ToString("yyyy-MM-dd"),
            cardHolder = scheme.CardHolder,
            approvalAmount = scheme.ApprovalAmount,
            approvalRemark = scheme.ApprovalRemark,
        });

        return JsonSerializer.Serialize(rows);
    }
}
