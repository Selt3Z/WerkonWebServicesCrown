using Crown.Infrastructure.Settings;

namespace Crown.Infrastructure.Documentation;

public sealed class DocumentationTrashRetentionHostedService : BackgroundService
{
    private static readonly TimeSpan RunInterval = TimeSpan.FromHours(6);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentationTrashRetentionHostedService> _logger;

    public DocumentationTrashRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentationTrashRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var purge = scope.ServiceProvider.GetRequiredService<DocumentationTrashPurgeService>();
                var settings = scope.ServiceProvider.GetRequiredService<AppSettingsService>();
                var retentionDays = await settings.GetDocumentationTrashRetentionDaysAsync(stoppingToken);
                var deleted = await purge.PurgeExpiredAsync(stoppingToken);
                if (deleted > 0)
                {
                    _logger.LogInformation(
                        "Purged {DeletedCount} documentation trash items older than {RetentionDays} days.",
                        deleted,
                        retentionDays);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Documentation trash retention cleanup failed.");
            }

            await Task.Delay(RunInterval, stoppingToken);
        }
    }
}
