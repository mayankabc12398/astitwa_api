using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class EmployeeRepository : RepositoryBase, IEmployeeRepository
{
    public EmployeeRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<EmployeeListItem>> ListAsync(PageRequest page, CancellationToken ct = default)
        => QueryPagedAsync<EmployeeListItem>("sp_hr_employee_list", page, ct: ct);

    public Task<Employee?> GetAsync(int employeeId, CancellationToken ct = default)
        => QuerySingleAsync<Employee>(
            "sp_hr_employee_get",
            ProcArgs.New().Set("employee_id", employeeId),
            ct);

    public Task<Employee?> SaveAsync(Employee employee, CancellationToken ct = default)
        => ExecuteReturningAsync<Employee>(
            "sp_hr_employee_save",
            ProcArgs.New()
                .Set("employee_id", employee.EmployeeId)
                .Set("employee_code", employee.EmployeeCode)
                .Set("full_name", employee.FullName)
                .Set("dob", employee.Dob)
                .Set("date_of_joining", employee.DateOfJoining)
                .Set("department_id", NullIfZero(employee.DepartmentId))
                .Set("designation_id", NullIfZero(employee.DesignationId))
                .Set("reporting_manager_id", NullIfZero(employee.ReportingManagerId))
                .Set("mobile", employee.Mobile)
                .Set("email", employee.Email)
                .Set("employment_status", employee.EmploymentStatus),
            ct);

    public Task DeleteAsync(int employeeId, CancellationToken ct = default)
        => ExecuteAsync("sp_hr_employee_delete", ProcArgs.New().Set("employee_id", employeeId), ct);

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => QueryAsync<LookupItem>("sp_hr_employee_lookup", ct: ct);

    public async Task<bool> CodeExistsAsync(string employeeCode, int employeeId, CancellationToken ct = default)
    {
        var count = await ScalarAsync<int?>(
            "sp_hr_employee_code_exists",
            ProcArgs.New()
                .Set("employee_code", employeeCode)
                .Set("employee_id", employeeId),
            ct).ConfigureAwait(false);

        return (count ?? 0) > 0;
    }

    /// <summary>A cleared dropdown posts 0; the column wants NULL.</summary>
    private static int? NullIfZero(int? value) => value is null or 0 ? null : value;
}
