using HrSuite.Common.Guards;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;

namespace HrSuite.Core.Services;

/// <summary>
/// Layer 1 rules for a vacancy: it names a role, and it asks for at least one person.
///
/// Everything else a hospital wants to insist on — a budget before approval, a department
/// that must be staffed, a target date inside the quarter — is that hospital's rule and
/// belongs in a cfg_field_rule row or a beforeSave script, not compiled in here.
///
/// The requisition code is issued by the database from a number series when it is left blank,
/// the same way a UHID is: a recruiter should not be inventing identifiers.
/// </summary>
public sealed class JobRequisitionService : IJobRequisitionService
{
    private readonly IJobRequisitionRepository _repository;

    public JobRequisitionService(IJobRequisitionRepository repository) => _repository = repository;

    public Task<PagedResult<JobRequisition>> ListAsync(PageRequest page, CancellationToken ct = default)
        => _repository.ListAsync(page, ct);

    public async Task<Result<JobRequisition>> GetAsync(int id, CancellationToken ct = default)
    {
        var found = await _repository.GetAsync(id, ct).ConfigureAwait(false);
        return found is null
            ? Result<JobRequisition>.NotFound("Requisition not found.")
            : Result<JobRequisition>.Success(found);
    }

    public async Task<Result<JobRequisition>> SaveAsync(JobRequisition requisition, CancellationToken ct = default)
    {
        var validation = new Validator()
            .RequireText(requisition.JobTitle, "A job title is required.", "jobTitle")
            .Require(requisition.Openings > 0, "At least one opening is required.", "openings")
            .ToResult();

        if (validation.IsFailure) return Result<JobRequisition>.Fail(validation.Errors.ToArray());

        requisition.JobTitle = requisition.JobTitle.Trim();
        requisition.RequisitionCode = (requisition.RequisitionCode ?? string.Empty).Trim();
        requisition.ExperienceRange = requisition.ExperienceRange?.Trim();
        requisition.KeySkills = requisition.KeySkills?.Trim();
        requisition.Notes = requisition.Notes?.Trim();

        // A budget that reads backwards is a typo every time, and it survives review because
        // both numbers look reasonable on their own.
        if (requisition.BudgetMin is not null && requisition.BudgetMax is not null
            && requisition.BudgetMin > requisition.BudgetMax)
        {
            return Result<JobRequisition>.Fail(
                Error.Validation("The budget range starts above where it ends.", "budgetMin"));
        }

        var saved = await _repository.SaveAsync(requisition, ct).ConfigureAwait(false);
        return saved is null
            ? Result<JobRequisition>.NotFound("Requisition not found.")
            : Result<JobRequisition>.Success(saved);
    }

    public async Task<Result> DeleteAsync(int id, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }
}
