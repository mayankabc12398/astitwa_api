using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

public sealed class DepartmentService : IDepartmentService
{
    private readonly IDepartmentRepository _repository;

    public DepartmentService(IDepartmentRepository repository) => _repository = repository;

    public Task<PagedResult<Department>> ListAsync(PageRequest page, CancellationToken ct = default)
        => _repository.ListAsync(page, ct);

    public async Task<Result<Department>> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        return found is null ? Result<Department>.NotFound("Department not found.") : Result<Department>.Success(found);
    }

    public async Task<Result<Department>> SaveAsync(Department department, CancellationToken ct = default)
    {
        var validation = new Validator()
            .RequireText(department.DeptCode, "Code is required.", "deptCode")
            .RequireText(department.DeptName, "Name is required.", "deptName")
            .ToResult();

        if (validation.IsFailure) return Result<Department>.Fail(validation.Errors.ToArray());

        department.DeptCode = department.DeptCode.Trim();
        department.DeptName = department.DeptName.Trim();

        Department? saved;
        try
        {
            saved = await _repository.SaveAsync(department, ct).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            // Code is unique within the tenant, and the index is what says so. Naming the
            // field puts the message on the input the user has to change.
            return Result<Department>.Fail(
                Error.Validation($"Department code '{department.DeptCode}' is already in use.", "deptCode"));
        }

        return saved is null
            ? Result<Department>.NotFound("Department not found.")
            : Result<Department>.Success(saved);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => _repository.LookupAsync(ct);
}
