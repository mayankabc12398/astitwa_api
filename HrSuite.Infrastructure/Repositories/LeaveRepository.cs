using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Repositories;

public sealed class LeaveRepository : RepositoryBase, ILeaveRepository
{
    public LeaveRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<LeaveRequestListItem>> ListAsync(
        PageRequest page, string? status, int? employeeId, CancellationToken ct = default)
        => QueryPagedAsync<LeaveRequestListItem>(
            "sp_hr_leave_request_list",
            page,
            ProcArgs.New()
                .Set("status", string.IsNullOrWhiteSpace(status) ? null : status)
                .Set("employee_id", employeeId),
            ct);

    public Task<LeaveRequestDetail?> GetAsync(int leaveRequestId, CancellationToken ct = default)
        => QuerySingleAsync<LeaveRequestDetail>(
            "sp_hr_leave_request_get",
            ProcArgs.New().Set("leave_request_id", leaveRequestId),
            ct);

    public Task<LeaveRequest?> SaveAsync(LeaveRequest request, CancellationToken ct = default)
        => ExecuteReturningAsync<LeaveRequest>(
            "sp_hr_leave_request_save",
            ProcArgs.New()
                .Set("leave_request_id", request.LeaveRequestId)
                .Set("employee_id", request.EmployeeId)
                .Set("leave_type_id", request.LeaveTypeId)
                .Set("from_date", request.FromDate)
                .Set("to_date", request.ToDate)
                .Set("days", request.Days)
                .Set("reason", request.Reason),
            ct);

    public Task<LeaveRequestDetail?> DecideAsync(
        int leaveRequestId, string status, string? remark, int? approverEmployeeId, CancellationToken ct = default)
        => ExecuteReturningAsync<LeaveRequestDetail>(
            "sp_hr_leave_request_approve",
            ProcArgs.New()
                .Set("leave_request_id", leaveRequestId)
                .Set("status", status)
                .Set("remark", remark)
                .Set("approver_emp_id", approverEmployeeId),
            ct);

    public Task<IReadOnlyList<LookupItem>> LeaveTypeLookupAsync(CancellationToken ct = default)
        => QueryAsync<LookupItem>("sp_hr_leave_type_lookup", ct: ct);
}
