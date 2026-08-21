using System.Data;
using Microsoft.Extensions.Options;
using MySqlConnector;

namespace HrSuite.Infrastructure.Data;

public sealed class MySqlConnectionFactory : IDbConnectionFactory
{
    private readonly DatabaseOptions _options;

    public MySqlConnectionFactory(IOptions<DatabaseOptions> options)
    {
        _options = options.Value;

        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException(
                "No database connection string. Set Database:ConnectionString via user-secrets " +
                "or the HRSUITE_Database__ConnectionString environment variable.");
        }
    }

    public int CommandTimeoutSeconds => _options.CommandTimeoutSeconds;

    public async Task<IDbConnection> OpenAsync(CancellationToken ct = default)
    {
        var connection = new MySqlConnection(_options.ConnectionString);
        await connection.OpenAsync(ct).ConfigureAwait(false);
        return connection;
    }
}
