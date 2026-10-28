using Microsoft.EntityFrameworkCore;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;

namespace Crown.Infrastructure.Audit;

public sealed class AuditRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<AuditRetentionHostedService> _logger;

    public AuditRetentionHostedService(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<AuditRetentionHostedService> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredEntriesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Audit retention cleanup failed.");
            }

            await Task.Delay(RunInterval, stoppingToken);
        }
    }

    private async Task PurgeExpiredEntriesAsync(CancellationToken cancellationToken)
    {
        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var appSettingsService = scope.ServiceProvider.GetRequiredService<AppSettingsService>();

        var retentionDays = await appSettingsService.GetAuditRetentionDaysAsync(cancellationToken);
        var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);

        var deletedCount = await dbContext.AuditLogEntries
            .Where(x => x.OccurredAtUtc < cutoffUtc)
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation(
                "Purged {DeletedCount} audit log entries older than {RetentionDays} days.",
                deletedCount,
                retentionDays);
        }
    }
}
