using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Hr;
using HrSuite.Core.Repositories;
using HrSuite.Infrastructure.Data;
using HrSuite.Infrastructure.Schema;

namespace HrSuite.Infrastructure.Repositories;

/// <summary>
/// Job requisitions: the columns the product ships through stored procedures, and whatever
/// the Screen Field Builder added to the same table alongside them.
///
/// The extra columns are read and written by name against the live table rather than from a
/// compiled list, because the whole point of the feature is that this code does not know what
/// they are. A key that is not a live cf_ column is dropped rather than rejected — a browser
/// tab left open across a field being deleted should not fail somebody's save.
/// </summary>
public sealed class JobRequisitionRepository : RepositoryBase, IJobRequisitionRepository
{
    private const string Table = "hr_job_requisition";
    private const string KeyColumn = "requisition_id";

    private readonly ISchemaExecutor _schema;

    public JobRequisitionRepository(IDbConnectionFactory factory, ITenantContext tenant, ISchemaExecutor schema)
        : base(factory, tenant)
        => _schema = schema;

    public Task<PagedResult<JobRequisition>> ListAsync(PageRequest page, CancellationToken ct = default)
        => QueryPagedAsync<JobRequisition>("sp_hr_job_requisition_list", page, ct: ct);

    public async Task<JobRequisition?> GetAsync(int requisitionId, CancellationToken ct = default)
    {
        var requisition = await QuerySingleAsync<JobRequisition>(
            "sp_hr_job_requisition_get",
            ProcArgs.New().Set("requisition_id", requisitionId),
            ct).ConfigureAwait(false);

        if (requisition is null) return null;

        var extraColumns = await BuilderColumnsAsync(ct).ConfigureAwait(false);
        if (extraColumns.Count > 0)
        {
            var values = await _schema
                .ReadExtraAsync(Table, KeyColumn, requisitionId, extraColumns, ct)
                .ConfigureAwait(false);

            foreach (var (key, value) in values) requisition.Extra[key] = value;
        }

        return requisition;
    }

    public async Task<JobRequisition?> SaveAsync(JobRequisition requisition, CancellationToken ct = default)
    {
        var saved = await ExecuteReturningAsync<JobRequisition>(
            "sp_hr_job_requisition_save",
            ProcArgs.New()
                .Set("requisition_id", requisition.RequisitionId)
                .Set("requisition_code", string.IsNullOrWhiteSpace(requisition.RequisitionCode) ? null : requisition.RequisitionCode)
                .Set("job_title", requisition.JobTitle)
                .Set("department_id", requisition.DepartmentId)
                .Set("openings", requisition.Openings)
                .Set("experience_range", requisition.ExperienceRange)
                .Set("employment_type", requisition.EmploymentType)
                .Set("priority", requisition.Priority)
                .Set("key_skills", requisition.KeySkills)
                .Set("budget_min", requisition.BudgetMin)
                .Set("budget_max", requisition.BudgetMax)
                .Set("target_date", requisition.TargetDate)
                .Set("notes", requisition.Notes)
                .Set("status", requisition.Status),
            ct).ConfigureAwait(false);

        if (saved is null) return null;

        // The builder's columns are written second, against the row the procedure has just
        // created — a new requisition has no id until then.
        if (requisition.Extra.Count > 0)
        {
            var live = await BuilderColumnsAsync(ct).ConfigureAwait(false);
            var known = live.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var writable = requisition.Extra
                .Where(pair => known.Contains(pair.Key))
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

            if (writable.Count > 0)
            {
                await _schema.WriteExtraAsync(Table, KeyColumn, saved.RequisitionId, writable, ct).ConfigureAwait(false);
            }
        }

        return await GetAsync(saved.RequisitionId, ct).ConfigureAwait(false) ?? saved;
    }

    public Task DeleteAsync(int requisitionId, CancellationToken ct = default)
        => ExecuteAsync("sp_hr_job_requisition_delete", ProcArgs.New().Set("requisition_id", requisitionId), ct);

    /// <summary>The cf_ columns the table actually has right now.</summary>
    private async Task<IReadOnlyList<string>> BuilderColumnsAsync(CancellationToken ct)
    {
        var columns = await _schema.ColumnsOfAsync(Table, ct).ConfigureAwait(false);
        return columns
            .Where(c => c.StartsWith(ColumnDdl.CustomPrefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
