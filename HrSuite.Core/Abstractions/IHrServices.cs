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

public interface IDocumentService
{
    Task<PagedResult<DocumentListItem>> ListAsync(PageRequest page, string? status, int? employeeId, CancellationToken ct = default);
    Task<Result<Document>> GetAsync(int id, CancellationToken ct = default);
    Task<Result<Document>> SaveAsync(Document document, CancellationToken ct = default);

    /// <summary>
    /// Moves a document between statuses. The transition is checked here rather than trusted
    /// to the caller — a revoked letter must not quietly become issued again.
    /// </summary>
    Task<Result<Document>> ChangeStatusAsync(DocumentStatusChange change, CancellationToken ct = default);

    /// <summary>Everything the client renderer needs to lay one document out.</summary>
    Task<Result<DocumentPrintContext>> PrintContextAsync(int id, CancellationToken ct = default);

    /// <summary>Counts across the whole register, for the headline figures and the gallery.</summary>
    Task<DocumentStats> StatsAsync(CancellationToken ct = default);

    Task<Result> DeleteAsync(int id, CancellationToken ct = default);
}
