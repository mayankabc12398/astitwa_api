using HrSuite.Common.Results;
using HrSuite.Core.Domain.Hr;

namespace HrSuite.Core.Repositories;

/// <summary>
/// Data contracts for the base HR scope. Implementations live in HrSuite.Infrastructure and
/// stamp the tenant themselves — no method here takes a tenant id, because no caller may supply one.
/// </summary>
public interface IDepartmentRepository
{
    Task<PagedResult<Department>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<Department?> GetAsync(int departmentId, CancellationToken ct = default);
    Task<Department?> SaveAsync(Department department, CancellationToken ct = default);
    Task DeleteAsync(int departmentId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default);
}

public interface IDesignationRepository
{
    Task<PagedResult<Designation>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<Designation?> GetAsync(int designationId, CancellationToken ct = default);
    Task<Designation?> SaveAsync(Designation designation, CancellationToken ct = default);
    Task DeleteAsync(int designationId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default);
}

public interface IPatientRepository
{
    Task<PagedResult<Patient>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<Patient?> GetAsync(int patientId, CancellationToken ct = default);
    Task<Patient?> SaveAsync(Patient patient, CancellationToken ct = default);
    Task DeleteAsync(int patientId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string patientCode, int patientId, CancellationToken ct = default);
}

public interface IEmployeeRepository
{
    Task<PagedResult<EmployeeListItem>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<Employee?> GetAsync(int employeeId, CancellationToken ct = default);
    Task<Employee?> SaveAsync(Employee employee, CancellationToken ct = default);
    Task DeleteAsync(int employeeId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string employeeCode, int employeeId, CancellationToken ct = default);
}

public interface ILeaveRepository
{
    Task<PagedResult<LeaveRequestListItem>> ListAsync(
        PageRequest page, string? status, int? employeeId, CancellationToken ct = default);

    Task<LeaveRequestDetail?> GetAsync(int leaveRequestId, CancellationToken ct = default);
    Task<LeaveRequest?> SaveAsync(LeaveRequest request, CancellationToken ct = default);
    Task<LeaveRequestDetail?> DecideAsync(int leaveRequestId, string status, string? remark, int? approverEmployeeId, CancellationToken ct = default);
    Task<IReadOnlyList<LookupItem>> LeaveTypeLookupAsync(CancellationToken ct = default);
}

public interface IDocumentRepository
{
    Task<PagedResult<DocumentListItem>> ListAsync(
        PageRequest page, string? status, int? employeeId, CancellationToken ct = default);

    Task<Document?> GetAsync(int documentId, CancellationToken ct = default);
    Task<Document?> SaveAsync(Document document, CancellationToken ct = default);
    Task<Document?> SetStatusAsync(int documentId, string status, string? deliveredVia, string? payloadJson, CancellationToken ct = default);

    /// <summary>The document, its employee and that employee's printable custom values.</summary>
    Task<DocumentPrintContext?> PrintContextAsync(int documentId, CancellationToken ct = default);

    /// <summary>Counts across the whole register, which a paged list cannot supply.</summary>
    Task<DocumentStats> StatsAsync(CancellationToken ct = default);

    Task DeleteAsync(int documentId, CancellationToken ct = default);
}

/// <summary>Job requisitions, including whatever columns the field builder added to them.</summary>
public interface IJobRequisitionRepository
{
    Task<PagedResult<JobRequisition>> ListAsync(PageRequest page, CancellationToken ct = default);
    Task<JobRequisition?> GetAsync(int requisitionId, CancellationToken ct = default);
    Task<JobRequisition?> SaveAsync(JobRequisition requisition, CancellationToken ct = default);
    Task DeleteAsync(int requisitionId, CancellationToken ct = default);
}
