using System.Data;
using System.Text.Json;
using Dapper;
using HrSuite.Infrastructure.Data;

namespace HrSuite.Infrastructure.Schema;

/// <summary>What the field builder needs the database to do that a stored procedure cannot.</summary>
public interface ISchemaExecutor
{
    /// <summary>The live columns of a table, lowercased.</summary>
    Task<IReadOnlySet<string>> ColumnsOfAsync(string table, CancellationToken ct = default);

    /// <summary>Runs one statement built by <see cref="ColumnDdl"/>. Returns the statement it ran.</summary>
    Task<string> RunAsync(string statement, CancellationToken ct = default);

    /// <summary>Reads (key, value) for one column, so the values survive the column being dropped.</summary>
    Task<IReadOnlyList<(string RecordId, string? Value)>> ReadColumnAsync(
        string table, string pkColumn, string column, CancellationToken ct = default);

    /// <summary>The builder-added columns of one record, keyed by column name.</summary>
    Task<IDictionary<string, object?>> ReadExtraAsync(
        string table, string pkColumn, object recordId, IReadOnlyList<string> columns, CancellationToken ct = default);

    /// <summary>Writes the builder-added columns of one record. Returns how many were written.</summary>
    Task<int> WriteExtraAsync(
        string table, string pkColumn, object recordId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default);
}

/// <summary>
/// The one place in the product that sends SQL text to the database.
///
/// "Stored procedures only" is the rule everywhere else, and it is enforced by an architecture
/// test; this file is named in that test's exemption list beside the API Builder's SqlGuard.
/// The rule exists so no ordinary data path can be talked into running text, and this is not
/// an ordinary data path: a schema change cannot be expressed as a stored procedure without
/// putting a string-to-execute parameter on one, which would be strictly worse — the guard
/// would then live in SQL, where it cannot be unit-tested.
///
/// What stands in for the rule here: every statement is built by <see cref="ColumnDdl"/> from
/// identifiers that were validated against a regex and resolved from the registry, never from
/// a request; nothing is concatenated in this file; and every statement is audited by the
/// caller whether it succeeded or not.
/// </summary>
public sealed class SchemaExecutor : ISchemaExecutor
{
    private readonly IDbConnectionFactory _factory;

    public SchemaExecutor(IDbConnectionFactory factory) => _factory = factory;

    public async Task<IReadOnlySet<string>> ColumnsOfAsync(string table, CancellationToken ct = default)
    {
        if (!ColumnDdl.IsPlainIdentifier(table))
            throw new InvalidOperationException($"Unsafe table identifier '{table}'.");

        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);

        // Parameterised, not interpolated: the table name travels as a value here, which is
        // what makes this read safe regardless of what the registry holds.
        var rows = await connection.QueryAsync<string>(new CommandDefinition(
            "SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @Table;",
            new { Table = table },
            commandType: CommandType.Text,
            commandTimeout: _factory.CommandTimeoutSeconds,
            cancellationToken: ct)).ConfigureAwait(false);

        return rows.Select(c => c.ToLowerInvariant()).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<string> RunAsync(string statement, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(statement))
            throw new InvalidOperationException("Refusing to run an empty statement.");

        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            statement,
            commandType: CommandType.Text,
            commandTimeout: _factory.CommandTimeoutSeconds,
            cancellationToken: ct)).ConfigureAwait(false);

        return statement;
    }

    public async Task<IDictionary<string, object?>> ReadExtraAsync(
        string table, string pkColumn, object recordId, IReadOnlyList<string> columns, CancellationToken ct = default)
    {
        if (columns.Count == 0) return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        var sql = ColumnDdl.BuildSelectExtra(table, pkColumn, columns);

        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        var row = await connection.QueryFirstOrDefaultAsync(new CommandDefinition(
            sql,
            new { RecordId = recordId },
            commandType: CommandType.Text,
            commandTimeout: _factory.CommandTimeoutSeconds,
            cancellationToken: ct)).ConfigureAwait(false);

        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (row is IDictionary<string, object?> dictionary)
        {
            foreach (var (key, value) in dictionary) values[key] = value;
        }
        return values;
    }

    public async Task<int> WriteExtraAsync(
        string table, string pkColumn, object recordId, IReadOnlyDictionary<string, object?> values, CancellationToken ct = default)
    {
        if (values.Count == 0) return 0;

        var columns = values.Keys.ToList();
        var sql = ColumnDdl.BuildUpdateExtra(table, pkColumn, columns);

        var parameters = new DynamicParameters();
        parameters.Add("RecordId", recordId);
        for (var i = 0; i < columns.Count; i++) parameters.Add($"v{i}", AsParameter(values[columns[i]]));

        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            sql,
            parameters,
            commandType: CommandType.Text,
            commandTimeout: _factory.CommandTimeoutSeconds,
            cancellationToken: ct)).ConfigureAwait(false);

        return columns.Count;
    }

    /// <summary>
    /// The value in a shape a parameter can hold.
    ///
    /// These values arrive from a JSON body as JsonElement, which Dapper refuses outright —
    /// and a builder-added column has no compiled property to deserialise into, so there is
    /// nothing earlier in the pipeline that could have unwrapped it.
    /// </summary>
    private static object? AsParameter(object? value) => value switch
    {
        null => null,
        JsonElement element => element.ValueKind switch
        {
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt64(out var whole) ? whole : element.GetDecimal(),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            // An object or an array in a scalar column is the caller's mistake; storing the
            // text of it keeps the save working and makes the mistake visible in the data.
            _ => element.ToString(),
        },
        _ => value,
    };

    public async Task<IReadOnlyList<(string RecordId, string? Value)>> ReadColumnAsync(
        string table, string pkColumn, string column, CancellationToken ct = default)
    {
        var sql = ColumnDdl.BuildReadColumn(table, pkColumn, column);

        using var connection = await _factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await connection.QueryAsync(new CommandDefinition(
            sql,
            commandType: CommandType.Text,
            commandTimeout: _factory.CommandTimeoutSeconds,
            cancellationToken: ct)).ConfigureAwait(false);

        return rows
            .Cast<IDictionary<string, object?>>()
            .Select(r => (
                RecordId: Convert.ToString(r["record_id"]) ?? string.Empty,
                Value: r["value_text"] is null ? null : Convert.ToString(r["value_text"])))
            .ToList();
    }
}
