using System.Data;
using Dapper;
using MySqlConnector;
using HrSuite.Common.Results;
using HrSuite.Core.Abstractions;

namespace HrSuite.Infrastructure.Data;

/// <summary>
/// Every repository derives from this. Two rules are enforced here rather than trusted to callers:
///
///   1. The tenant filter. p_tenant_id is stamped from the request context on every call.
///      A repository method physically cannot run without it.
///   2. Stored procedures only. There is no overload on this class that accepts SQL text,
///      so no derived class can pass one.
/// </summary>
public abstract class RepositoryBase
{
    private readonly IDbConnectionFactory _factory;
    private readonly ITenantContext _tenant;

    protected RepositoryBase(IDbConnectionFactory factory, ITenantContext tenant)
    {
        _factory = factory;
        _tenant = tenant;
    }

    protected int TenantId => Resolved().TenantId;
    protected int UserId => Resolved().UserId;

    private ITenantContext Resolved()
    {
        if (!_tenant.IsResolved)
        {
            throw new InvalidOperationException(
                "No tenant on this request. A repository was reached before tenant resolution ran.");
        }
        return _tenant;
    }

    // -----------------------------------------------------------------
    // Reads
    // -----------------------------------------------------------------

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string procName, ProcArgs? args = null, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync<T>(Command(procName, args, ct)).ConfigureAwait(false);
        return rows.AsList();
    }

    protected async Task<T?> QuerySingleAsync<T>(string procName, ProcArgs? args = null, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.QueryFirstOrDefaultAsync<T>(Command(procName, args, ct)).ConfigureAwait(false);
    }

    /// <summary>
    /// For the paging convention: the procedure returns the page as its first result set and
    /// a single total-count scalar as its second. Section 11 — no list endpoint is unbounded.
    /// </summary>
    protected async Task<PagedResult<T>> QueryPagedAsync<T>(
        string procName, PageRequest page, ProcArgs? args = null, CancellationToken ct = default)
    {
        var withPaging = (args ?? ProcArgs.New())
            .Set("page_size", page.PageSize)
            .Set("offset", page.Offset)
            .Set("search", string.IsNullOrWhiteSpace(page.Search) ? null : page.Search.Trim());

        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        using var grid = await connection.QueryMultipleAsync(Command(procName, withPaging, ct)).ConfigureAwait(false);

        var items = (await grid.ReadAsync<T>().ConfigureAwait(false)).AsList();
        var total = await grid.ReadFirstOrDefaultAsync<int?>().ConfigureAwait(false) ?? items.Count;

        return PagedResult<T>.Create(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// For the named-query runner, where the row shape is not known until run time.
    /// Still a stored-procedure call and still tenant-stamped; only the column list is open.
    /// </summary>
    protected async Task<IReadOnlyList<IDictionary<string, object?>>> QueryDynamicAsync(
        string procName, ProcArgs? args = null, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(Command(procName, args, ct)).ConfigureAwait(false);

        return rows
            .Cast<IDictionary<string, object?>>()
            .Select(row => (IDictionary<string, object?>)new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Reads two result sets in one round trip.</summary>
    protected async Task<(IReadOnlyList<T1> First, IReadOnlyList<T2> Second)> QueryTwoAsync<T1, T2>(
        string procName, ProcArgs? args = null, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        using var grid = await connection.QueryMultipleAsync(Command(procName, args, ct)).ConfigureAwait(false);

        var first = (await grid.ReadAsync<T1>().ConfigureAwait(false)).AsList();
        var second = (await grid.ReadAsync<T2>().ConfigureAwait(false)).AsList();
        return (first, second);
    }

    // -----------------------------------------------------------------
    // Writes
    // -----------------------------------------------------------------

    protected async Task<int> ExecuteAsync(string procName, ProcArgs? args = null, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        return await Translating(procName,
            () => connection.ExecuteAsync(Command(procName, args, ct))).ConfigureAwait(false);
    }

    /// <summary>For save procedures that return the affected row so the API can echo it back.</summary>
    protected async Task<T?> ExecuteReturningAsync<T>(string procName, ProcArgs? args = null, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        return await Translating(procName,
            () => connection.QueryFirstOrDefaultAsync<T>(Command(procName, args, ct))).ConfigureAwait(false);
    }

    protected async Task<TScalar?> ScalarAsync<TScalar>(string procName, ProcArgs? args = null, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        return await Translating(procName,
            () => connection.ExecuteScalarAsync<TScalar>(Command(procName, args, ct))).ConfigureAwait(false);
    }

    /// <summary>
    /// Turns a unique-constraint violation into a domain exception on the way out.
    ///
    /// Without this a duplicate code reaches the client as a 500 with a trace id, which says
    /// "we broke" when the truth is "you already used that code". Every write goes through
    /// one of the three helpers above, so no repository can skip the translation, and a table
    /// added later is covered the day its unique index is.
    /// </summary>
    private static async Task<T> Translating<T>(string procName, Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
        {
            throw new DuplicateKeyException(ConstraintNameFrom(ex.Message), procName, ex);
        }
    }

    /// <summary>
    /// Pulls the index name out of "Duplicate entry '1-OPS' for key 'hr_department.uk_hr_department'".
    /// The message is all MySQL gives; if the shape ever changes the caller still gets a
    /// duplicate-key failure, just without the index name.
    /// </summary>
    private static string ConstraintNameFrom(string message)
    {
        const string marker = "for key '";
        var start = message.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return "unknown";

        start += marker.Length;
        var end = message.IndexOf("'", start, StringComparison.Ordinal);
        var name = end < 0 ? message[start..] : message[start..end];

        // MySQL 8 qualifies the index with its table: take the part that names the index.
        var dot = name.LastIndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    // -----------------------------------------------------------------

    private CommandDefinition Command(string procName, ProcArgs? args, CancellationToken ct)
    {
        var context = Resolved();
        return new CommandDefinition(
            commandText: procName,
            parameters: (args ?? ProcArgs.New()).ToDynamicParameters(context.TenantId, context.UserId),
            commandType: CommandType.StoredProcedure,
            commandTimeout: _factory.CommandTimeoutSeconds,
            cancellationToken: ct);
    }
}
