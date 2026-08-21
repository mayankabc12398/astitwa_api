using HrSuite.Common.Results;
using HrSuite.Core.Domain.Hr;

namespace HrSuite.Core.Abstractions;

public interface IDepartmentService
{
    Task<PagedResult<Department>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<Result<Department>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Department>> SaveAsync(Department department, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default);
}

public interface IDesignationService
{
    Task<PagedResult<Designation>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<Result<Designation>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Designation>> SaveAsync(Designation designation, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default);
}

public interface IEmployeeService
{
    Task<PagedResult<EmployeeListItem>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<Result<Employee>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Employee>> SaveAsync(Employee employee, CancellationToken ct = default);
    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default);
}

public interface ILeaveService
{
    Task<PagedResult<LeaveRequestListItem>> ListAsync(PageRequest page, string? status, int? employeeId, CancellationToken ct = default);
    Task<Result<LeaveRequestDetail>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<LeaveRequest>> SaveAsync(LeaveRequest request, CancellationToken ct = default);
    Task<Result<LeaveRequestDetail>> DecideAsync(LeaveDecision decision, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LeaveTypeLookupAsync(CancellationToken ct = default);
}
