using System.Data;

namespace HrSuite.Infrastructure.Data;

public interface IDbConnectionFactory
{
    Task<IDbConnection> OpenAsync(CancellationToken ct = default);
    int CommandTimeoutSeconds { get; }
}
