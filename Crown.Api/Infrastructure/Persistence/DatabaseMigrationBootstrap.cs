using Microsoft.EntityFrameworkCore;

namespace Crown.Infrastructure.Persistence;

public static class DatabaseMigrationBootstrap
{
    /// <summary>
    /// Применяет миграции до того, как Kestrel начнёт принимать запросы.
    /// </summary>
    public static async Task ApplyPendingMigrationsAsync(
        WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseMigration");

        // Integration-тесты подменяют EF на InMemory — миграции там не поддерживаются.
        if (!db.Database.IsRelational())
        {
            logger.LogInformation("Skipping migrations: non-relational database provider.");
            return;
        }

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date.");
            return;
        }

        logger.LogInformation(
            "Applying {Count} pending migration(s): {Migrations}",
            pending.Count,
            string.Join(", ", pending));

        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("Database migrations applied.");
    }
}
