using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class DesignationRepository : RepositoryBase, IDesignationRepository
{
    public DesignationRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<Designation>> ListAsync(PageRequest page, CancellationToken ct = default)
        => QueryPagedAsync<Designation>("sp_hr_designation_list", page, ct: ct);

    public Task<Designation?> GetAsync(int designationId, CancellationToken ct = default)
        => QuerySingleAsync<Designation>(
            "sp_hr_designation_get",
            ProcArgs.New().Set("designation_id", designationId),
            ct);

    public Task<Designation?> SaveAsync(Designation designation, CancellationToken ct = default)
        => ExecuteReturningAsync<Designation>(
            "sp_hr_designation_save",
            ProcArgs.New()
                .Set("designation_id", designation.DesignationId)
                .Set("desig_code", designation.DesigCode)
                .Set("desig_name", designation.DesigName)
                .Set("grade", designation.Grade),
            ct);

    public Task DeleteAsync(int designationId, CancellationToken ct = default)
        => ExecuteAsync("sp_hr_designation_delete", ProcArgs.New().Set("designation_id", designationId), ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => QueryAsync<LookupItem>("sp_hr_designation_lookup", ct: ct);
}
