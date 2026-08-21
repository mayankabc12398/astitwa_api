using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Addons.Payroll.Data;

/// <summary>
/// The add-on's own data access, on the product's own repository base. It inherits the
/// tenant filter for free: a licensed module still cannot read another tenant's rows.
/// </summary>
public sealed class PayrollRunRepository : RepositoryBase
{
    public PayrollRunRepository(IDbConnectionFactory factory, ITenantContext tenant) : base(factory, tenant) { }

    public Task<PagedResult<PayrollRun>> ListAsync(PageRequest page, CancellationToken ct)
        => QueryPagedAsync<PayrollRun>("sp_pay_run_list", page, ct: ct);
}

public sealed class PayrollRun
{
    public int PayrollRunId { get; set; }
    public string PeriodLabel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int EmployeeCount { get; set; }
    public DateTime? RunOn { get; set; }
}
