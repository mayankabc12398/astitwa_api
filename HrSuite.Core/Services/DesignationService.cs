using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

public sealed class DesignationService : IDesignationService
{
    private readonly IDesignationRepository _repository;

    public DesignationService(IDesignationRepository repository) => _repository = repository;

    public Task<PagedResult<Designation>> ListAsync(PageRequest page, CancellationToken ct = default)
        => _repository.ListAsync(page, ct);

    public async Task<Result<Designation>> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        return found is null ? Result<Designation>.NotFound("Designation not found.") : Result<Designation>.Success(found);
    }

    public async Task<Result<Designation>> SaveAsync(Designation designation, CancellationToken ct = default)
    {
        var validation = new Validator()
            .RequireText(designation.DesigCode, "Code is required.", "desigCode")
            .RequireText(designation.DesigName, "Name is required.", "desigName")
            .ToResult();

        if (validation.IsFailure) return Result<Designation>.Fail(validation.Errors.ToArray());

        designation.DesigCode = designation.DesigCode.Trim();
        designation.DesigName = designation.DesigName.Trim();
        designation.Grade = designation.Grade?.Trim();

        Designation? saved;
        try
        {
            saved = await _repository.SaveAsync(designation, ct).ConfigureAwait(false);
        }
        catch (DuplicateKeyException)
        {
            return Result<Designation>.Fail(
                Error.Validation($"Designation code '{designation.DesigCode}' is already in use.", "desigCode"));
        }

        return saved is null
            ? Result<Designation>.NotFound("Designation not found.")
            : Result<Designation>.Success(saved);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<IReadOnlyList<LookupItem>> LookupAsync(CancellationToken ct = default)
        => _repository.LookupAsync(ct);
}
