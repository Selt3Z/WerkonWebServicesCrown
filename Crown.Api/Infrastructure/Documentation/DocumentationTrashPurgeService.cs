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
        return await PurgeAsync(libraryId: null, olderThan: cutoff, cancellationToken);
    }

    public Task<int> PurgeLibraryAsync(Guid libraryId, CancellationToken cancellationToken = default) =>
        PurgeAsync(libraryId, olderThan: null, cancellationToken);

    private async Task<int> PurgeAsync(
        Guid? libraryId,
        DateTimeOffset? olderThan,
        CancellationToken cancellationToken)
    {
        var filesQuery = _db.DocumentationFiles.Where(x => x.DeletedAt != null);
        var foldersQuery = _db.DocumentationFolders.Where(x => x.DeletedAt != null);
        if (libraryId is Guid lid)
        {
            filesQuery = filesQuery.Where(x => x.LibraryId == lid);
            foldersQuery = foldersQuery.Where(x => x.LibraryId == lid);
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
