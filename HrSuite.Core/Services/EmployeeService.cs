using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Extensibility;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 employee rules — true for every customer, no client name anywhere.
///
///   * Employee code is unique per tenant.
///
/// Anything more specific than that (a joining date that may not be in the future, a
/// mandatory reporting manager, a mobile-number format) is deliberately NOT here. Those
/// belong to Layer 2 as a field rule or Layer 5 as a beforeSave script.
/// </summary>
public sealed class EmployeeService : IEmployeeService
{
    private readonly IEmployeeRepository _repository;
    private readonly HookInvoker _hooks;

    public EmployeeService(IEmployeeRepository repository, HookInvoker hooks)
    {
        _repository = repository;
        _hooks = hooks;
    }

    public Task<PagedResult<EmployeeListItem>> ListAsync(PageRequest page, CancellationToken ct = default)
        => _repository.ListAsync(page, ct);

    public async Task<Result<Employee>> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        if (found is null) return Result<Employee>.NotFound("Employee not found.");

        await _hooks.RunAsync(HookKeys.EmployeeOnLoad, form: found, ct: ct).ConfigureAwait(false);
        return Result<Employee>.Success(found);
    }

    public async Task<Result<Employee>> SaveAsync(Employee employee, CancellationToken ct = default)
    {
        var validation = new Validator()
            .RequireText(employee.EmployeeCode, "Employee code is required.", "employeeCode")
            .RequireText(employee.FullName, "Name is required.", "fullName")
            .ToResult();

        if (validation.IsFailure) return Result<Employee>.Fail(validation.Errors.ToArray());

        employee.EmployeeCode = employee.EmployeeCode.Trim();
        employee.FullName = employee.FullName.Trim();
        employee.Mobile = employee.Mobile?.Trim();
        employee.Email = employee.Email?.Trim();

        // Layer 1 rule: employee code is unique within the tenant.
        if (await _repository.CodeExistsAsync(employee.EmployeeCode, employee.EmployeeId, ct).ConfigureAwait(false))
        {
            return Result<Employee>.Fail(
                Error.Validation($"Employee code '{employee.EmployeeCode}' is already in use.", "employeeCode"));
        }

        if (employee.ReportingManagerId is int manager && manager == employee.EmployeeId && employee.EmployeeId != 0)
        {
            return Result<Employee>.Fail(
                Error.Validation("An employee cannot report to themselves.", "reportingManagerId"));
        }

        // Layer 5 slot. With no script registered this returns an empty result and the
        // save proceeds exactly as written.
        var before = await _hooks.RunAsync(HookKeys.EmployeeBeforeSave, form: employee, ct: ct).ConfigureAwait(false);
        if (before.CancelSave)
        {
            return Result<Employee>.Invalid(before.Message ?? "The save was cancelled by a configured rule.");
        }

        ApplyScriptEdits(employee, before);

        var saved = await _repository.SaveAsync(employee, ct).ConfigureAwait(false);
        if (saved is null) return Result<Employee>.NotFound("Employee not found.");

        await _hooks.RunAsync(HookKeys.EmployeeAfterSave, form: employee, response: saved, ct: ct).ConfigureAwait(false);

        return Result<Employee>.Success(saved);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => _repository.LookupAsync(ct);

    /// <summary>
    /// A script may adjust the record through ctx.setForm(). Only fields the script is
    /// allowed to touch are copied back; identity and the tenant are never among them.
    /// </summary>
    private static void ApplyScriptEdits(Employee employee, HookResult result)
    {
        if (result.Form is null || result.Form.Count == 0) return;

        foreach (var (key, value) in result.Form)
        {
            switch (key.ToLowerInvariant())
            {
                case "fullname": employee.FullName = value?.ToString() ?? employee.FullName; break;
                case "mobile": employee.Mobile = value?.ToString(); break;
                case "email": employee.Email = value?.ToString(); break;
                case "employmentstatus": employee.EmploymentStatus = value?.ToString() ?? employee.EmploymentStatus; break;
                case "departmentid": employee.DepartmentId = AsInt(value); break;
                case "designationid": employee.DesignationId = AsInt(value); break;
                case "reportingmanagerid": employee.ReportingManagerId = AsInt(value); break;
                case "dob": employee.Dob = AsDate(value); break;
                case "dateofjoining": employee.DateOfJoining = AsDate(value); break;
                default: break; // employeeCode, employeeId and anything unknown are ignored on purpose
            }
        }
    }

    private static int? AsInt(object? value)
        => value is null ? null : int.TryParse(value.ToString(), out var parsed) ? parsed : null;

    private static DateTime? AsDate(object? value)
        => value switch
        {
            null => null,
            DateTime date => date,
            _ => DateTime.TryParse(value.ToString(), out var parsed) ? parsed : null
        };
}
