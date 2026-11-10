using Microsoft.EntityFrameworkCore;
using Crown.Infrastructure.Attachments;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;

namespace Crown.Infrastructure.Documentation;

/// <summary>Безвозвратное удаление элементов корзины документации (диск + БД).</summary>
public sealed class DocumentationTrashPurgeService
{
    private readonly AppDbContext _db;
    private readonly AttachmentStorageService _storage;
    private readonly AppSettingsService _settings;

    public DocumentationTrashPurgeService(
        AppDbContext db,
        AttachmentStorageService storage,
        AppSettingsService settings)
    {
        _db = db;
        _storage = storage;
        _settings = settings;
    }

    public async Task<int> PurgeExpiredAsync(CancellationToken cancellationToken = default)
    {
        var retentionDays = await _settings.GetDocumentationTrashRetentionDaysAsync(cancellationToken);
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        return await PurgeAsync(ownerUserId: null, olderThan: cutoff, cancellationToken);
    }

    public Task<int> PurgeSectionAsync(Guid ownerUserId, CancellationToken cancellationToken = default) =>
        PurgeAsync(ownerUserId, olderThan: null, cancellationToken);

    private async Task<int> PurgeAsync(
        Guid? ownerUserId,
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken)
    {
        var filesQuery = _db.DocumentationFiles.Where(x => x.DeletedAt != null);
        var foldersQuery = _db.DocumentationFolders.Where(x => x.DeletedAt != null);
        if (ownerUserId is Guid oid)
        {
            filesQuery = filesQuery.Where(x => x.OwnerUserId == oid);
            foldersQuery = foldersQuery.Where(x => x.OwnerUserId == oid);
        }

        if (olderThan is DateTimeOffset cutoff)
        {
            filesQuery = filesQuery.Where(x => x.DeletedAt < cutoff);
            foldersQuery = foldersQuery.Where(x => x.DeletedAt < cutoff);
        }

        var files = await filesQuery.ToListAsync(cancellationToken);
        var paths = files.Select(x => x.StoragePath).ToList();
        _db.DocumentationFiles.RemoveRange(files);

        var folders = await foldersQuery.ToListAsync(cancellationToken);
        _db.DocumentationFolders.RemoveRange(folders);

        await _db.SaveChangesAsync(cancellationToken);

        foreach (var path in paths)
        {
            _storage.DeleteIfExists(path);
        }

        return files.Count + folders.Count;
    }
}
