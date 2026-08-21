using HrSuite.Common.Helpers;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Extensibility;
using HrSuite.Core.Repositories;
using Microsoft.Extensions.Logging;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 leave rules — true for every customer:
///
///   * A to-date may not precede its from-date.
///   * An employee may not approve their own leave request.
///
/// Entitlement balances, approval chains and carry-forward are all client-specific and
/// belong to Layer 5, not here.
/// </summary>
public sealed class LeaveService : ILeaveService
{
    private readonly ILeaveRepository _repository;
    private readonly HookInvoker _hooks;
    private readonly ITenantContext _tenant;
    private readonly INotificationDispatcher _notifications;
    private readonly ITemplateRenderer _templates;
    private readonly ILogger<LeaveService> _log;

    public LeaveService(
        ILeaveRepository repository,
        HookInvoker hooks,
        ITenantContext tenant,
        INotificationDispatcher notifications,
        ITemplateRenderer templates,
        ILogger<LeaveService> log)
    {
        _repository = repository;
        _hooks = hooks;
        _tenant = tenant;
        _notifications = notifications;
        _templates = templates;
        _log = log;
    }

    public Task<PagedResult<LeaveRequestListItem>> ListAsync(
        PageRequest page, string? status, int? employeeId, CancellationToken ct = default)
        => _repository.ListAsync(page, status, employeeId, ct);

    public async Task<Result<LeaveRequestDetail>> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        return found is null
            ? Result<LeaveRequestDetail>.NotFound("Leave request not found.")
            : Result<LeaveRequestDetail>.Success(found);
    }

    public async Task<Result<LeaveRequest>> SaveAsync(LeaveRequest request, CancellationToken ct = default)
    {
        if (request.EmployeeId <= 0) return Result<LeaveRequest>.Invalid("Employee is required.", "employeeId");
        if (request.LeaveTypeId <= 0) return Result<LeaveRequest>.Invalid("Leave type is required.", "leaveTypeId");

        // Layer 1 rule: the range must run forwards.
        if (request.ToDate.Date < request.FromDate.Date)
        {
            return Result<LeaveRequest>.Invalid("The to-date cannot be earlier than the from-date.", "toDate");
        }

        if (request.Days <= 0) request.Days = DateHelper.InclusiveDays(request.FromDate, request.ToDate);

        var before = await _hooks.RunAsync(HookKeys.LeaveRequestBeforeSave, form: request, ct: ct).ConfigureAwait(false);
        if (before.CancelSave)
        {
            return Result<LeaveRequest>.Invalid(before.Message ?? "The save was cancelled by a configured rule.");
        }

        var saved = await _repository.SaveAsync(request, ct).ConfigureAwait(false);
        if (saved is null) return Result<LeaveRequest>.NotFound("Leave request not found, or it is no longer pending.");

        await _hooks.RunAsync(HookKeys.LeaveRequestAfterSave, form: request, response: saved, ct: ct).ConfigureAwait(false);

        return Result<LeaveRequest>.Success(saved);
    }

    public async Task<Result<LeaveRequestDetail>> DecideAsync(LeaveDecision decision, CancellationToken ct = default)
    {
        var existing = await _repository.GetAsync(decision.LeaveRequestId, ct).ConfigureAwait(false);
        if (existing is null) return Result<LeaveRequestDetail>.NotFound("Leave request not found.");

        if (!string.Equals(existing.Status, LeaveStatus.Pending, StringComparison.OrdinalIgnoreCase))
        {
            return Result<LeaveRequestDetail>.Fail(
                Error.Conflict($"This request has already been {existing.Status.ToLowerInvariant()}."));
        }

        // Layer 1 rule: nobody approves their own leave.
        if (_tenant.EmployeeId is int approver && approver == existing.EmployeeId)
        {
            return Result<LeaveRequestDetail>.Fail(
                Error.Forbidden("You cannot approve or reject your own leave request."));
        }

        var status = decision.Approve ? LeaveStatus.Approved : LeaveStatus.Rejected;

        var updated = await _repository
            .DecideAsync(decision.LeaveRequestId, status, decision.Remark, _tenant.EmployeeId, ct)
            .ConfigureAwait(false);

        if (updated is null) return Result<LeaveRequestDetail>.NotFound("Leave request not found.");

        await NotifyAsync(updated, ct).ConfigureAwait(false);

        return Result<LeaveRequestDetail>.Success(updated);
    }

    public Task<IReadOnlyList<LookupItem>> LeaveTypeLookupAsync(CancellationToken ct = default)
        => _repository.LeaveTypeLookupAsync(ct);

    /// <summary>
    /// Layer 4 boundary. With no integration enabled the dispatcher reports "skipped" and the
    /// approval still succeeded — the notification is never allowed to fail the decision
    /// (section 9, acceptance scenario 7).
    /// </summary>
    private async Task NotifyAsync(LeaveRequestDetail request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.EmployeeEmail)) return;

        var tokens = new Dictionary<string, object?>
        {
            ["employeeName"] = request.EmployeeName,
            ["status"] = request.Status,
            ["fromDate"] = DateHelper.IsoDate(request.FromDate),
            ["toDate"] = DateHelper.IsoDate(request.ToDate),
            ["days"] = request.Days,
            ["remark"] = request.ApprovalRemark ?? string.Empty,
            ["leaveType"] = request.LeaveTypeName ?? string.Empty
        };

        var subject = await _templates
            .RenderAsync("template.leave.decision.subject", tokens, "Your leave request was {{status}}", ct)
            .ConfigureAwait(false);

        var body = await _templates
            .RenderAsync(
                "template.leave.decision.body",
                tokens,
                "Hello {{employeeName}}, your leave from {{fromDate}} to {{toDate}} ({{days}} day(s)) was {{status}}. {{remark}}",
                ct)
            .ConfigureAwait(false);

        var result = await _notifications
            .DispatchAsync(new NotificationMessage(request.EmployeeEmail!, subject, body, "leave.decision", tokens), ct)
            .ConfigureAwait(false);

        if (result.Skipped)
        {
            _log.LogDebug("Leave decision notification skipped for request {Id}: {Reason}",
                request.LeaveRequestId, result.Reason);
        }
    }
}
