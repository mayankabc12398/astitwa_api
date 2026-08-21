using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class DepartmentRepository : RepositoryBase, IDepartmentRepository
{
    public DepartmentRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<Department>> ListAsync(PageRequest page, CancellationToken ct = default)
        => QueryPagedAsync<Department>("sp_hr_department_list", page, ct: ct);

    public Task<Department?> GetAsync(int departmentId, CancellationToken ct = default)
        => QuerySingleAsync<Department>(
            "sp_hr_department_get",
            ProcArgs.New().Set("department_id", departmentId),
            ct);

    public Task<Department?> SaveAsync(Department department, CancellationToken ct = default)
        => ExecuteReturningAsync<Department>(
            "sp_hr_department_save",
            ProcArgs.New()
                .Set("department_id", department.DepartmentId)
                .Set("dept_code", department.DeptCode)
                .Set("dept_name", department.DeptName),
            ct);

    public Task DeleteAsync(int departmentId, CancellationToken ct = default)
        => ExecuteAsync("sp_hr_department_delete", ProcArgs.New().Set("department_id", departmentId), ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => QueryAsync<LookupItem>("sp_hr_department_lookup", ct: ct);
}
