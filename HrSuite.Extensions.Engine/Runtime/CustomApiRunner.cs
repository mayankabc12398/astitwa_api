using System.Data;
using System.Diagnostics;
using Dapper;
using HrSuite.Core.Abstractions;
using HrSuite.Core.Domain.Identity;
using HrSuite.Extensions.Engine.Data;
using HrSuite.Extensions.Engine.Models;
using HrSuite.Infrastructure.Data;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace HrSuite.Extensions.Engine.Runtime;

/// <summary>
/// The only place in the product that executes SQL text rather than a stored procedure.
///
/// RepositoryBase has no overload that takes SQL, on purpose — so no repository can be
/// talked into running text. This class exists because the API Builder's whole point is
/// that the statement is data, and it pays for that with four separate defences:
///
///   1. SqlGuard, run again here rather than trusted from save time. A row edited straight
///      in the database has never seen the screen's validation.
///   2. Bound parameters only. Nothing a caller sends is ever concatenated into the text;
///      the {tenant} token becomes a bound parameter like any other.
///   3. A READ ONLY transaction. MySQL itself refuses a write inside one, so a verb that
///      somehow reads as harmless to the guard still cannot change a row.
///   4. The column whitelist and the row cap, applied on the way out — the cap by the
///      database, in the wrapper SqlGuard.Compile builds, rather than by fetching
///      everything and discarding most of it.
/// </summary>
public sealed class CustomApiRunner : ICustomApiCaller
{
    private readonly IDbConnectionFactory _factory;
    private readonly CustomApiRepository _repository;
    private readonly ITenantContext _tenant;
    private readonly ILogger<CustomApiRunner> _log;

    public CustomApiRunner(
        IDbConnectionFactory factory,
        CustomApiRepository repository,
        ITenantContext tenant,
        ILogger<CustomApiRunner> log)
    {
        _factory = factory;
        _repository = repository;
        _tenant = tenant;
        _log = log;
    }

    /// <summary>Serves one call to /api/x/{slug}.</summary>
    public async Task<CustomApiResult> RunAsync(
        string slug, IDictionary<string, object?>? supplied, CancellationToken ct = default)
    {
        if (!SqlGuard.IsValidSlug(slug))
            return CustomApiResult.Failure($"'{slug}' is not an endpoint.");

        var endpoint = await _repository.GetBySlugAsync(slug, ct).ConfigureAwait(false);

        // Inactive, deleted, another tenant's, or never created: one answer for all four.
        // Telling a caller which would let them map what exists.
        if (endpoint is null)
            return CustomApiResult.Failure($"'{slug}' is not an endpoint.");

        if (!string.IsNullOrWhiteSpace(endpoint.RequiredPermission) && !_tenant.Has(endpoint.RequiredPermission!))
        {
            await LogAsync(endpoint, CustomApiCallStatus.Denied, 0, 0,
                $"Caller lacks {endpoint.RequiredPermission}.", ct).ConfigureAwait(false);

            return CustomApiResult.Failure($"You do not have permission to call '{slug}'.");
        }

        var declared = CustomApiRepository.ParamsOf(endpoint);
        var whitelist = CustomApiRepository.ColumnsOf(endpoint);

        var verdict = SqlGuard.Check(endpoint.SqlText, declared);
        if (!verdict.Ok)
        {
            _log.LogWarning("Endpoint {Slug} is registered with SQL its own rules refuse: {Reason}", slug, verdict.Error);
            await LogAsync(endpoint, CustomApiCallStatus.Error, 0, 0, verdict.Error, ct).ConfigureAwait(false);

            // The caller is not the author and cannot act on the detail.
            return CustomApiResult.Failure($"'{slug}' cannot be run.");
        }

        var result = await ExecuteAsync(
            endpoint.SqlText, declared, supplied, endpoint.MaxRows, whitelist, revealErrors: false, ct)
            .ConfigureAwait(false);

        await LogAsync(
            endpoint,
            result.Ok ? CustomApiCallStatus.Ok : CustomApiCallStatus.Error,
            result.DurationMs,
            result.Rows.Count,
            result.Error,
            ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>
    /// The admin screen's Test button. Two things differ from a live call, and both are
    /// because the caller is the author: an empty whitelist means "show me every column",
    /// which is how the screen offers them to choose from, and the database's own error text
    /// comes back so a typo is fixable without reading a server log.
    /// </summary>
    public async Task<CustomApiResult> TestAsync(CustomApiTestRequest request, CancellationToken ct = default)
    {
        var verdict = SqlGuard.Check(request.SqlText, request.Params);
        if (!verdict.Ok) return CustomApiResult.Failure(verdict.Error!);

        var supplied = request.Params.ToDictionary(
            p => p.Name,
            p => (object?)p.Sample,
            StringComparer.OrdinalIgnoreCase);

        var maxRows = request.MaxRows is < 1 or > 200 ? 25 : request.MaxRows;

        return await ExecuteAsync(
            request.SqlText, request.Params, supplied, maxRows, request.Columns, revealErrors: true, ct)
            .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------

    private async Task<CustomApiResult> ExecuteAsync(
        string sqlText,
        IReadOnlyList<CustomApiParam> declared,
        IDictionary<string, object?>? supplied,
        int maxRows,
        IReadOnlyList<string> whitelist,
        bool revealErrors,
        CancellationToken ct)
    {
        var cap = maxRows is < 1 or > 1000 ? 100 : maxRows;
        var sent = supplied ?? new Dictionary<string, object?>();
        var parameters = new DynamicParameters();

        foreach (var param in declared)
        {
            var present = TryFind(sent, param.Name, out var raw);

            if (param.Required && (!present || raw is null))
                return CustomApiResult.Failure($"'{param.Name}' is required.");

            parameters.Add(param.Name, present ? ParamCoercion.To(raw, param.Type) : null);
        }

        // The tenant comes from the request context, never from the caller. This is the same
        // guarantee RepositoryBase gives every stored-procedure call in the product.
        parameters.Add(SqlGuard.TenantParam, _tenant.TenantId);

        // One more than the cap, so "there were more" is a fact rather than a guess: a page
        // exactly the size of the cap is otherwise indistinguishable from a truncated one.
        parameters.Add(SqlGuard.MaxRowsParam, cap + 1);

        var started = Stopwatch.StartNew();

        try
        {
            using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);

            // The last line of defence. Everything above is our own code being careful;
            // this is the database refusing to write no matter what got through.
            await connection.ExecuteAsync(new CommandDefinition(
                "START TRANSACTION READ ONLY", cancellationToken: ct)).ConfigureAwait(false);

            try
            {
                var raw = await connection.QueryAsync(new CommandDefinition(
                    SqlGuard.Compile(sqlText),
                    parameters,
                    commandType: CommandType.Text,
                    commandTimeout: _factory.CommandTimeoutSeconds,
                    cancellationToken: ct)).ConfigureAwait(false);

                var rows = raw
                    .Cast<IDictionary<string, object?>>()
                    .Select(row => (IDictionary<string, object?>)new Dictionary<string, object?>(row, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                started.Stop();

                var truncated = rows.Count > cap;
                var page = truncated ? rows.Take(cap).ToList() : rows;

                return Project(page, whitelist, truncated, (int)started.ElapsedMilliseconds);
            }
            finally
            {
                // Nothing to commit — the transaction exists to forbid writes, not to hold any.
                await connection.ExecuteAsync("ROLLBACK").ConfigureAwait(false);
            }
        }
        catch (MySqlException ex)
        {
            started.Stop();
            _log.LogError(ex, "A custom endpoint failed for tenant {TenantId}.", _tenant.TenantId);

            return CustomApiResult.Failure(
                revealErrors ? Explain(ex) : "The endpoint could not be run.",
                (int)started.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            started.Stop();
            _log.LogError(ex, "A custom endpoint failed for tenant {TenantId}.", _tenant.TenantId);

            return CustomApiResult.Failure(
                revealErrors ? ex.Message : "The endpoint could not be run.",
                (int)started.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Strips every column the registration did not declare.
    ///
    /// An empty whitelist returns nothing at all for a live call — silence is not
    /// permission, and a SELECT * that grew a salary column should not start publishing it.
    /// The test path passes the columns it wants, or none to see the shape first.
    /// </summary>
    private static CustomApiResult Project(
        IReadOnlyList<IDictionary<string, object?>> rows,
        IReadOnlyList<string> whitelist,
        bool truncated,
        int durationMs)
    {
        if (whitelist.Count == 0)
        {
            var seen = rows.Count == 0
                ? Array.Empty<string>()
                : rows[0].Keys.ToArray();

            return new CustomApiResult(true, rows, seen, truncated, durationMs);
        }

        var allowed = whitelist
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projected = rows
            .Select(row => (IDictionary<string, object?>)row
                .Where(kv => allowed.Contains(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            .ToList();

        return new CustomApiResult(true, projected, allowed.ToList(), truncated, durationMs);
    }

    /// <summary>
    /// Turns the two failures an author actually hits into something they can act on.
    /// Everything else is passed through — it is their own SQL, and they wrote it.
    /// </summary>
    private static string Explain(MySqlException ex) => ex.ErrorCode switch
    {
        MySqlErrorCode.DuplicateFieldName =>
            "Two columns come back with the same name. Give each one its own alias — SELECT a.name AS dept_name.",
        MySqlErrorCode.NoSuchTable =>
            $"{ex.Message} Check the table name; an endpoint can only read this database.",
        _ => ex.Message
    };

    private static bool TryFind(IDictionary<string, object?> supplied, string name, out object? value)
    {
        if (supplied.TryGetValue(name, out value)) return true;

        foreach (var (key, candidate) in supplied)
        {
            if (!string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = candidate;
            return true;
        }

        value = null;
        return false;
    }

    private async Task LogAsync(
        CustomApiEndpoint endpoint, string status, int durationMs, int rowCount, string? message, CancellationToken ct)
    {
        try
        {
            await _repository.LogCallAsync(
                endpoint.EndpointId, endpoint.Slug, status, durationMs, rowCount, message, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // If even the audit write fails there is nothing further to do; the caller
            // already has their answer and losing the log entry is the lesser harm.
            _log.LogWarning(ex, "The call log for {Slug} could not be written.", endpoint.Slug);
        }
    }
}
