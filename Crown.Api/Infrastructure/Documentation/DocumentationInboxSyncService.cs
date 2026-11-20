using System.Collections.Concurrent;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Crown.Domain.Entities;
using Crown.Infrastructure.Attachments;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Documentation;

public sealed class DocumentationInboxSyncResult
{
    public Guid? OwnerUserId { get; set; }
    public string? FolderKey { get; set; }
    public string HostInboxPath { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int FoldersCreated { get; set; }
    public int PendingBefore { get; set; }
    public List<string> Messages { get; set; } = [];
}

public sealed class DocumentationInboxInfo
{
    public Guid OwnerUserId { get; set; }
    public string FolderKey { get; set; } = string.Empty;
    public string HostInboxPath { get; set; } = string.Empty;
    public string RelativeInboxPath { get; set; } = string.Empty;
    public int PendingFileCount { get; set; }
    public long MaxInboxFileBytes { get; set; }
}

/// <summary>
/// Импорт больших деревьев из share-папки docs-inbox/{логин}/ в раздел документации.
/// После успеха исходник переносится в .processed/{stamp}/.
/// </summary>
public sealed class DocumentationInboxSyncService
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly ConcurrentDictionary<Guid, byte> EnsuredOwners = new();
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    private static readonly HashSet<string> ReservedDirNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".processed",
        ".errors",
        ".tmp"
    };

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AttachmentStorageService _storage;
    private readonly ILogger<DocumentationInboxSyncService> _logger;

    public DocumentationInboxSyncService(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        AttachmentStorageService storage,
        ILogger<DocumentationInboxSyncService> logger)
    {
        _db = db;
        _users = users;
        _storage = storage;
        _logger = logger;
    }

    public async Task EnsureInboxLayoutAsync(CancellationToken cancellationToken)
    {
        var root = _storage.GetDocsInboxAbsoluteRoot();
        WriteReadmeIfMissing(root);

        var departments = await _users.GetUsersInRoleAsync(AppRoles.Department);
        foreach (var user in departments.Where(x => x.IsActive && x.DocumentationEnabled))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureOwnerFolders(user);
        }
    }

    public DocumentationInboxInfo GetInboxInfo(ApplicationUser owner)
    {
        var folderKey = ResolveFolderKey(owner);
        EnsureOwnerFolders(owner);
        var pending = 0;
        foreach (var dir in DistinctInboxDirs(owner, folderKey))
        {
            pending += CountPendingFiles(dir);
        }

        return new DocumentationInboxInfo
        {
            OwnerUserId = owner.Id,
            FolderKey = folderKey,
            HostInboxPath = _storage.GetDocsInboxHostPath(folderKey),
            RelativeInboxPath = $"{AttachmentStorageService.DocsInboxFolderName}/{folderKey}",
            PendingFileCount = pending,
            MaxInboxFileBytes = _storage.MaxInboxFileBytes
        };
    }

    public async Task<DocumentationInboxSyncResult> SyncOwnerAsync(
        Guid ownerUserId,
        Guid? uploadedByUserId,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            var owner = await _users.Users.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == ownerUserId, cancellationToken);
            if (owner is null || !owner.IsActive || !owner.DocumentationEnabled)
            {
                return new DocumentationInboxSyncResult
                {
                    OwnerUserId = ownerUserId,
                    Messages = ["Раздел не найден или документация выключена."]
                };
            }

            if (!await _users.IsInRoleAsync(owner, AppRoles.Department))
            {
                return new DocumentationInboxSyncResult
                {
                    OwnerUserId = ownerUserId,
                    Messages = ["Владелец раздела должен иметь роль Подразделение."]
                };
            }

            return await SyncOwnerCoreAsync(owner, uploadedByUserId, cancellationToken);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<DocumentationInboxSyncResult> SyncAllAsync(CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            await EnsureInboxLayoutAsync(cancellationToken);
            var departments = await _users.GetUsersInRoleAsync(AppRoles.Department);
            var owners = departments.Where(x => x.IsActive && x.DocumentationEnabled).ToList();
            var aggregate = new DocumentationInboxSyncResult
            {
                HostInboxPath = _storage.GetDocsInboxHostPath()
            };

            foreach (var owner in owners)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var one = await SyncOwnerCoreAsync(owner, uploadedByUserId: null, cancellationToken);
                aggregate.Imported += one.Imported;
                aggregate.Updated += one.Updated;
                aggregate.Skipped += one.Skipped;
                aggregate.Failed += one.Failed;
                aggregate.FoldersCreated += one.FoldersCreated;
                aggregate.PendingBefore += one.PendingBefore;
                foreach (var msg in one.Messages.Take(20))
                {
                    aggregate.Messages.Add($"{one.FolderKey}: {msg}");
                }
            }

            return aggregate;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<DocumentationInboxSyncResult> SyncOwnerCoreAsync(
        ApplicationUser owner,
        Guid? uploadedByUserId,
        CancellationToken cancellationToken)
    {
        var folderKey = ResolveFolderKey(owner);
        EnsureOwnerFolders(owner);
        var result = new DocumentationInboxSyncResult
        {
            OwnerUserId = owner.Id,
            FolderKey = folderKey,
            HostInboxPath = _storage.GetDocsInboxHostPath(folderKey)
        };

        var inboxDirs = DistinctInboxDirs(owner, folderKey);
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var files = new List<(string InboxRoot, string AbsolutePath, string RelativeKey)>();

        foreach (var inboxRoot in inboxDirs)
        {
            CollectPendingFiles(inboxRoot, inboxRoot, files);
        }

        result.PendingBefore = files.Count;
        if (files.Count == 0)
        {
            result.Messages.Add("Нет новых файлов в inbox.");
            return result;
        }

        // Кэш папок: относительный путь "a/b" → FolderId
        var folderCache = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        // Один SaveChanges на пачку — не N round-trips на файл.
        const int commitEvery = 40;
        var pendingMoves = new List<(string InboxRoot, string AbsolutePath, string RelativeKey)>();
        var dirty = 0;

        async Task FlushAsync()
        {
            if (dirty > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
                dirty = 0;
            }

            foreach (var move in pendingMoves)
            {
                MoveToBucket(move.InboxRoot, move.AbsolutePath, move.RelativeKey, ".processed", stamp);
            }

            pendingMoves.Clear();
        }

        foreach (var (inboxRoot, absolutePath, relativeKey) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var fileInfo = new FileInfo(absolutePath);
                if (!fileInfo.Exists)
                {
                    result.Skipped++;
                    continue;
                }

                if (fileInfo.Length > _storage.MaxInboxFileBytes)
                {
                    result.Failed++;
                    result.Messages.Add($"Слишком большой: {relativeKey} ({fileInfo.Length} байт).");
                    MoveToBucket(inboxRoot, absolutePath, relativeKey, ".errors", stamp);
                    continue;
                }

                if (fileInfo.Length <= 0)
                {
                    result.Skipped++;
                    MoveToBucket(inboxRoot, absolutePath, relativeKey, ".processed", stamp);
                    continue;
                }

                var dirKey = Path.GetDirectoryName(relativeKey)?.Replace('\\', '/') ?? string.Empty;
                Guid? folderId = null;
                if (!string.IsNullOrWhiteSpace(dirKey))
                {
                    folderId = await EnsureFolderPathAsync(
                        owner.Id,
                        dirKey,
                        folderCache,
                        result,
                        cancellationToken);
                }

                var existing = await _db.DocumentationFiles
                    .FirstOrDefaultAsync(
                        x => x.OwnerUserId == owner.Id
                             && x.InboxSourceKey == relativeKey
                             && x.DeletedAt == null,
                        cancellationToken);

                if (existing is not null && existing.SizeBytes == fileInfo.Length)
                {
                    result.Skipped++;
                    MoveToBucket(inboxRoot, absolutePath, relativeKey, ".processed", stamp);
                    continue;
                }

                await using (var stream = File.OpenRead(absolutePath))
                {
                    var (relativeStorage, size) = await _storage.SaveDocumentationAsync(
                        owner.Id,
                        folderId,
                        Path.GetFileName(absolutePath),
                        stream,
                        cancellationToken);

                    var contentType = GuessContentType(absolutePath);
                    if (existing is not null)
                    {
                        _storage.DeleteIfExists(existing.StoragePath);
                        existing.FileName = AttachmentStorageService.SanitizeFileNamePublic(Path.GetFileName(absolutePath));
                        existing.ContentType = contentType;
                        existing.SizeBytes = size;
                        existing.StoragePath = relativeStorage;
                        existing.FolderId = folderId;
                        existing.UploadedAt = DateTimeOffset.UtcNow;
                        existing.UploadedByUserId = uploadedByUserId;
                        result.Updated++;
                    }
                    else
                    {
                        _db.DocumentationFiles.Add(new DocumentationFile
                        {
                            Id = Guid.NewGuid(),
                            OwnerUserId = owner.Id,
                            FolderId = folderId,
                            FileName = AttachmentStorageService.SanitizeFileNamePublic(Path.GetFileName(absolutePath)),
                            ContentType = contentType,
                            SizeBytes = size,
                            StoragePath = relativeStorage,
                            InboxSourceKey = relativeKey,
                            UploadedAt = DateTimeOffset.UtcNow,
                            UploadedByUserId = uploadedByUserId
                        });
                        result.Imported++;
                    }
                }

                dirty++;
                pendingMoves.Add((inboxRoot, absolutePath, relativeKey));
                if (dirty >= commitEvery)
                {
                    await FlushAsync();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Откатываем незакоммиченный трекер, чтобы один битый файл не сорвал пачку.
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }

                dirty = 0;
                pendingMoves.Clear();
                result.Failed++;
                result.Messages.Add($"{relativeKey}: {ex.Message}");
                _logger.LogWarning(ex, "Inbox import failed for {Owner} / {Key}", owner.Id, relativeKey);
                try
                {
                    MoveToBucket(inboxRoot, absolutePath, relativeKey, ".errors", stamp);
                }
                catch (Exception moveEx)
                {
                    _logger.LogWarning(moveEx, "Failed to move errored inbox file {Path}", absolutePath);
                }
            }
        }

        await FlushAsync();
        return result;
    }

    private async Task<Guid> EnsureFolderPathAsync(
        Guid ownerUserId,
        string relativeDirKey,
        Dictionary<string, Guid> cache,
        DocumentationInboxSyncResult result,
        CancellationToken cancellationToken)
    {
        var normalized = relativeDirKey.Replace('\\', '/').Trim('/');
        if (cache.TryGetValue(normalized, out var cached))
        {
            return cached;
        }

        Guid? parentId = null;
        var built = new List<string>();
        foreach (var rawSegment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var segment = NormalizeFolderSegment(rawSegment);
            if (segment is null)
            {
                continue;
            }

            built.Add(segment);
            var pathKey = string.Join('/', built);
            if (cache.TryGetValue(pathKey, out var existingId))
            {
                parentId = existingId;
                continue;
            }

            var folder = await _db.DocumentationFolders
                .FirstOrDefaultAsync(
                    x => x.OwnerUserId == ownerUserId
                         && x.ParentFolderId == parentId
                         && x.Name == segment
                         && x.DeletedAt == null,
                    cancellationToken);

            if (folder is null)
            {
                folder = new DocumentationFolder
                {
                    Id = Guid.NewGuid(),
                    OwnerUserId = ownerUserId,
                    ParentFolderId = parentId,
                    Name = segment,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _db.DocumentationFolders.Add(folder);
                // Папку нужно зафиксировать до уникального FK/поиска детей в той же пачке.
                await _db.SaveChangesAsync(cancellationToken);
                result.FoldersCreated++;
            }

            cache[pathKey] = folder.Id;
            parentId = folder.Id;
        }

        if (parentId is null)
        {
            throw new InvalidOperationException($"Не удалось создать путь папки: {relativeDirKey}");
        }

        cache[normalized] = parentId.Value;
        return parentId.Value;
    }

    private List<string> DistinctInboxDirs(ApplicationUser owner, string folderKey)
    {
        var list = new List<string>
        {
            _storage.GetOwnerInboxAbsolutePath(folderKey)
        };
        var byId = _storage.GetOwnerInboxAbsolutePath(owner.Id.ToString("N"));
        if (!string.Equals(Path.GetFullPath(list[0]), Path.GetFullPath(byId), StringComparison.OrdinalIgnoreCase))
        {
            list.Add(byId);
        }

        return list;
    }

    private void EnsureOwnerFolders(ApplicationUser owner)
    {
        EnsuredOwners.TryAdd(owner.Id, 0);
        var key = ResolveFolderKey(owner);
        _storage.GetOwnerInboxAbsolutePath(key);
        _storage.GetOwnerInboxAbsolutePath(owner.Id.ToString("N"));
    }

    public static string ResolveFolderKey(ApplicationUser owner)
    {
        var login = AttachmentStorageService.SanitizeFolderSegment(owner.UserName ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(owner.UserName)
            && !string.Equals(login, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return login;
        }

        return owner.Id.ToString("N");
    }

    private static void CollectPendingFiles(
        string inboxRoot,
        string currentDir,
        List<(string InboxRoot, string AbsolutePath, string RelativeKey)> sink)
    {
        if (!Directory.Exists(currentDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(currentDir))
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith(".", StringComparison.Ordinal)
                || name.Equals("README.txt", StringComparison.OrdinalIgnoreCase)
                || name.Equals("README.md", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var relative = Path.GetRelativePath(inboxRoot, file).Replace('\\', '/');
            sink.Add((inboxRoot, file, relative));
        }

        foreach (var dir in Directory.EnumerateDirectories(currentDir))
        {
            var name = Path.GetFileName(dir);
            if (ReservedDirNames.Contains(name) || name.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            CollectPendingFiles(inboxRoot, dir, sink);
        }
    }

    private static int CountPendingFiles(string inboxRoot)
    {
        if (!Directory.Exists(inboxRoot))
        {
            return 0;
        }

        var list = new List<(string, string, string)>();
        CollectPendingFiles(inboxRoot, inboxRoot, list);
        return list.Count;
    }

    private static void MoveToBucket(
        string inboxRoot,
        string absolutePath,
        string relativeKey,
        string bucketName,
        string stamp)
    {
        if (!File.Exists(absolutePath))
        {
            return;
        }

        var targetRoot = Path.Combine(inboxRoot, bucketName, stamp);
        var target = Path.Combine(targetRoot, relativeKey.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        if (File.Exists(target))
        {
            File.Delete(target);
        }

        File.Move(absolutePath, target);

        // Чистим пустые каталоги вверх до корня inbox (не трогаем .processed/.errors).
        TryDeleteEmptyParents(Path.GetDirectoryName(absolutePath), inboxRoot);
    }

    private static void TryDeleteEmptyParents(string? startDir, string stopAt)
    {
        var stop = Path.GetFullPath(stopAt);
        var current = startDir is null ? null : Path.GetFullPath(startDir);
        while (!string.IsNullOrEmpty(current)
               && current.StartsWith(stop, StringComparison.OrdinalIgnoreCase)
               && !string.Equals(current, stop, StringComparison.OrdinalIgnoreCase))
        {
            if (Directory.Exists(current)
                && !Directory.EnumerateFileSystemEntries(current).Any())
            {
                Directory.Delete(current);
                current = Path.GetDirectoryName(current);
                continue;
            }

            break;
        }
    }

    private static string GuessContentType(string path)
    {
        if (ContentTypes.TryGetContentType(path, out var type) && !string.IsNullOrWhiteSpace(type))
        {
            return type;
        }

        return "application/octet-stream";
    }

    private static string? NormalizeFolderSegment(string raw)
    {
        var name = raw.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        if (name.Length > 200)
        {
            name = name[..200];
        }

        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static void WriteReadmeIfMissing(string root)
    {
        var readme = Path.Combine(root, "README.txt");
        if (File.Exists(readme))
        {
            return;
        }

        File.WriteAllText(readme,
            """
            Crown — inbox документации (share)
            ==================================
            Кладёте файлы/папки в подкаталог подразделения (логин или Guid N),
            API подхватывает их (фон ~5 мин или кнопка «Импорт из inbox» в UI).

            После импорта исходники уезжают в .processed/{дата_время}/.
            Ошибки — в .errors/{дата_время}/.

            Не трогайте каталоги .processed / .errors / .tmp.
            Подробности: docs/DOCUMENTATION-INBOX.md в репозитории.
            """);
    }
}
