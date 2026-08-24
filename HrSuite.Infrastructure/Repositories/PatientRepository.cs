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

    public Task<Patient?> GetAsync(int patientId, CancellationToken ct = default)
        => QuerySingleAsync<Patient>(
            "sp_hr_patient_get",
            ProcArgs.New().Set("patient_id", patientId),
            ct);

    public Task<Patient?> SaveAsync(Patient patient, CancellationToken ct = default)
        => ExecuteReturningAsync<Patient>(
            "sp_hr_patient_save",
            ProcArgs.New()
                .Set("patient_id", patient.PatientId)
                // Blank means "issue one": the procedure reads NULL and the empty
                // string the same way, but NULL is what the intent looks like.
                .Set("patient_code", string.IsNullOrWhiteSpace(patient.PatientCode) ? null : patient.PatientCode)
                .Set("full_name", patient.FullName)
                .Set("gender", patient.Gender)
                .Set("dob", patient.Dob)
                .Set("mobile", patient.Mobile)
                .Set("email", patient.Email)
                .Set("blood_group", patient.BloodGroup)
                .Set("address", patient.Address)
                .Set("city", patient.City)
                .Set("registered_on", patient.RegisteredOn),
            ct);

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
}
