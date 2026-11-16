namespace Crown.Infrastructure.Documentation;

/// <summary>Периодический импорт из docs-inbox (share) во все разделы документации.</summary>
public sealed class DocumentationInboxSyncHostedService : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan RunInterval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentationInboxSyncHostedService> _logger;

    public DocumentationInboxSyncHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DocumentationInboxSyncHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<DocumentationInboxSyncService>();
                await sync.EnsureInboxLayoutAsync(stoppingToken);
                var result = await sync.SyncAllAsync(stoppingToken);
                if (result.Imported > 0 || result.Updated > 0 || result.Failed > 0)
                {
                    _logger.LogInformation(
                        "Documentation inbox sync: imported={Imported}, updated={Updated}, skipped={Skipped}, failed={Failed}, folders={Folders}.",
                        result.Imported,
                        result.Updated,
                        result.Skipped,
                        result.Failed,
                        result.FoldersCreated);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Documentation inbox sync failed.");
            }

            try
            {
                await Task.Delay(RunInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
