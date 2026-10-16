using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using QueryForge.Infrastructure.Persistence;

namespace QueryForge.Api.Health;

public sealed class SqlServerHealthCheck : IHealthCheck
{
    // Kept below the registration timeout because SqlClient does not reliably honour cancellation while connecting.
    private const int ConnectTimeoutSeconds = 2;

    private readonly string _connectionString;

    public SqlServerHealthCheck(QueryForgeDbContext dbContext)
    {
        // A dedicated, unpooled connection tests real connectivity on every probe and is not affected by the
        // application pool's blocking period after an outage, so recovery is reported as soon as SQL Server is back.
        _connectionString = new SqlConnectionStringBuilder(dbContext.Database.GetConnectionString())
        {
            ConnectTimeout = ConnectTimeoutSeconds,
            Pooling = false
        }.ConnectionString;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(cancellationToken).WaitAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            await command.ExecuteScalarAsync(cancellationToken).WaitAsync(cancellationToken);

            return HealthCheckResult.Healthy("SQL Server is reachable.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception is logged by the health check service; only the generic description is returned to callers.
            return new HealthCheckResult(context.Registration.FailureStatus, "SQL Server is unreachable.", exception);
        }
    }
}
