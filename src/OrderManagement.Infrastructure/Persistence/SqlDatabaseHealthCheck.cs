using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrderManagement.Infrastructure.Persistence;

/// <summary>
/// Readiness: can this instance reach its database with its own connection string? It opens a plain
/// <see cref="SqlConnection"/> on purpose. EF's <c>CanConnectAsync</c> runs through the retrying execution strategy, so a
/// transient-classified error (e.g. an Azure SQL failover) would be retried with back-off and every probe would stall
/// until it timed out. A probe should answer "not right now" immediately.
/// (Microsoft's EF Core health-check package targets net9 only.)
/// </summary>
internal sealed class SqlDatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (SqlException ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "The database is unreachable.", ex);
        }
    }
}
