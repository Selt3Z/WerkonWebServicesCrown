using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Crown.Infrastructure.Persistence;

/// <summary>
/// Readiness: БД доступна и нет неприменённых EF-миграций (код и схема совпадают).
/// </summary>
public sealed class DatabaseMigrationHealthCheck(AppDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (!await dbContext.Database.CanConnectAsync(cancellationToken))
        {
            return HealthCheckResult.Unhealthy("Cannot connect to the database.");
        }

        var pending = (await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            return HealthCheckResult.Healthy();
        }

        return HealthCheckResult.Unhealthy(
            $"Pending EF migrations ({pending.Count}): {string.Join(", ", pending)}");
    }
}
