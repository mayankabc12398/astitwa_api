using System.Data;
using Dapper;

namespace HrSuite.Infrastructure.Data;

/// <summary>
/// The single, deliberate exception to the tenant filter: procedures that run BEFORE a tenant
/// is known. In practice that is login (which resolves the tenant from a tenant code) and
/// nothing else.
///
/// Derive from this only when there is no tenant to filter by yet. Every other repository
/// derives from <see cref="RepositoryBase"/>, where the filter is stamped automatically.
/// Each procedure used here must resolve the tenant itself from its own arguments.
/// </summary>
public abstract class UnscopedRepositoryBase
{
    private readonly IDbConnectionFactory _factory;

    protected UnscopedRepositoryBase(IDbConnectionFactory factory) => _factory = factory;

    /// <summary>Argument bag that may carry an explicit tenant id, since none is ambient yet.</summary>
    protected static ProcArgs Args() => ProcArgs.NewUnscoped();

    protected async Task<T?> QuerySingleAsync<T>(string procName, ProcArgs args, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.QueryFirstOrDefaultAsync<T>(Command(procName, args, ct)).ConfigureAwait(false);
    }

    protected async Task<IReadOnlyList<T>> QueryAsync<T>(string procName, ProcArgs args, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        return (await connection.QueryAsync<T>(Command(procName, args, ct)).ConfigureAwait(false)).AsList();
    }

    protected async Task<(IReadOnlyList<T1> First, IReadOnlyList<T2> Second)> QueryTwoAsync<T1, T2>(
        string procName, ProcArgs args, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        using var grid = await connection.QueryMultipleAsync(Command(procName, args, ct)).ConfigureAwait(false);

        var first = (await grid.ReadAsync<T1>().ConfigureAwait(false)).AsList();
        var second = (await grid.ReadAsync<T2>().ConfigureAwait(false)).AsList();
        return (first, second);
    }

    protected async Task<int> ExecuteAsync(string procName, ProcArgs args, CancellationToken ct = default)
    {
        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        return await connection.ExecuteAsync(Command(procName, args, ct)).ConfigureAwait(false);
    }

    private CommandDefinition Command(string procName, ProcArgs args, CancellationToken ct)
        => new(
            commandText: procName,
            parameters: args.ToDynamicParametersWithoutTenant(),
            commandType: CommandType.StoredProcedure,
            commandTimeout: _factory.CommandTimeoutSeconds,
            cancellationToken: ct);
}
