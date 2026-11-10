using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.Documentation;
using Crown.Contracts.Requests;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Attachments;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;

namespace Crown.Features.Documentation;

[ApiController]
[Route("api/documentation")]
[Authorize]
public sealed class DocumentationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly AttachmentStorageService _storage;
    private readonly AttachmentPreviewService _preview;
    private readonly AppSettingsService _settings;
    private readonly Crown.Infrastructure.Documentation.DocumentationTrashPurgeService _trashPurge;

    public DocumentationController(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        AttachmentStorageService storage,
        AttachmentPreviewService preview,
        AppSettingsService settings,
        Crown.Infrastructure.Documentation.DocumentationTrashPurgeService trashPurge)
    {
        _db = db;
        _users = users;
        _storage = storage;
        _preview = preview;
        _settings = settings;
        _trashPurge = trashPurge;
    }

    /// <summary>Подразделы — только роль Department с включённой документацией.</summary>
    [HttpGet("sections")]
    public async Task<ActionResult<List<DocumentationSectionResponse>>> ListSections(
        CancellationToken cancellationToken)
    {
        var me = GetUserId();
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var isDispatcher = User.IsInRole(AppRoles.Dispatcher);
        // Только роль Department; админы/диспетчеры в список разделов не попадают.
        var departmentUsers = await _users.GetUsersInRoleAsync(AppRoles.Department);
        var owners = departmentUsers
            .Where(x => x.IsActive && x.DocumentationEnabled)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .ToList();

        var ownerIds = owners.Select(x => x.Id).ToList();
        var stats = await _db.DocumentationFiles.AsNoTracking()
            .Where(f => ownerIds.Contains(f.OwnerUserId) && f.DeletedAt == null)
            .GroupBy(f => f.OwnerUserId)
            .Select(g => new { OwnerUserId = g.Key, FileCount = g.Count(), TotalSizeBytes = g.Sum(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.OwnerUserId, cancellationToken);

        return Ok(owners.Select(x =>
        {
            stats.TryGetValue(x.Id, out var st);
            return new DocumentationSectionResponse
            {
                UserId = x.Id,
                DisplayName = CleanSectionTitle(x.DisplayName),
                UserName = string.Empty,
                CanWrite = isAdmin || isDispatcher || me == x.Id,
                FileCount = st?.FileCount ?? 0,
                TotalSizeBytes = st?.TotalSizeBytes ?? 0
            };
        }).ToList());
    }

    [HttpGet("sections/{ownerUserId:guid}")]
    public async Task<ActionResult<DocumentationBrowseResponse>> Browse(
        Guid ownerUserId,
        [FromQuery] Guid? folderId,
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var owner = await _users.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == ownerUserId, cancellationToken);
        if (owner is null || !owner.IsActive || !owner.DocumentationEnabled)
        {
            return NotFound();
        }

        // Разделы — только у роли Department (админы/диспетчеры мимо).
        if (!await _users.IsInRoleAsync(owner, AppRoles.Department))
        {
            return NotFound();
        }

        DocumentationFolder? folder = null;
        if (folderId is Guid fid)
        {
            folder = await _db.DocumentationFolders.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == fid && x.OwnerUserId == ownerUserId && x.DeletedAt == null,
                    cancellationToken);
            if (folder is null)
            {
                return NotFound();
            }
        }

        var me = GetUserId();
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var isDispatcher = User.IsInRole(AppRoles.Dispatcher);
        var canWrite = isAdmin || isDispatcher || me == ownerUserId;
        var canViewTrash = isAdmin || me == ownerUserId;
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        List<DocumentationFolderItemResponse> folders;
        List<DocumentationFileItemResponse> files;

        if (search is not null)
        {
            // Поиск по всему разделу подразделения (не только текущая папка).
            var allFolders = await _db.DocumentationFolders.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId && x.DeletedAt == null)
                .Select(x => new { x.Id, x.Name, x.ParentFolderId, x.CreatedAt })
                .ToListAsync(cancellationToken);
            var byId = allFolders.ToDictionary(x => x.Id);
            string PathOf(Guid? id)
            {
                if (id is null)
                {
                    return "Корень";
                }

                var parts = new List<string>();
                var guard = 0;
                var cur = id;
                while (cur is Guid cid && byId.TryGetValue(cid, out var node) && guard++ < 64)
                {
                    parts.Add(node.Name);
                    cur = node.ParentFolderId;
                }

                parts.Reverse();
                return string.Join(" / ", parts);
            }

            var term = search.ToLowerInvariant();
            folders = allFolders
                .Where(x => x.Name.ToLowerInvariant().Contains(term))
                .OrderBy(x => x.Name)
                .Select(x => new DocumentationFolderItemResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    ParentFolderId = x.ParentFolderId,
                    CreatedAt = x.CreatedAt,
                    PathLabel = PathOf(x.ParentFolderId)
                })
                .ToList();

            var like = $"%{EscapeLike(search)}%";
            files = await _db.DocumentationFiles.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId
                            && x.DeletedAt == null
                            && EF.Functions.ILike(x.FileName, like))
                .OrderBy(x => x.FileName)
                .Select(x => new DocumentationFileItemResponse
                {
                    Id = x.Id,
                    FileName = x.FileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    FolderId = x.FolderId,
                    UploadedAt = x.UploadedAt
                })
                .ToListAsync(cancellationToken);
            foreach (var f in files)
            {
                f.PathLabel = PathOf(f.FolderId);
            }
        }
        else
        {
            folders = await _db.DocumentationFolders.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId
                            && x.ParentFolderId == folderId
                            && x.DeletedAt == null)
                .OrderBy(x => x.Name)
                .Select(x => new DocumentationFolderItemResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    ParentFolderId = x.ParentFolderId,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            files = await _db.DocumentationFiles.AsNoTracking()
                .Where(x => x.OwnerUserId == ownerUserId
                            && x.FolderId == folderId
                            && x.DeletedAt == null)
                .OrderBy(x => x.FileName)
                .Select(x => new DocumentationFileItemResponse
                {
                    Id = x.Id,
                    FileName = x.FileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    FolderId = x.FolderId,
                    UploadedAt = x.UploadedAt
                })
                .ToListAsync(cancellationToken);
        }

        return Ok(new DocumentationBrowseResponse
        {
            OwnerUserId = ownerUserId,
            OwnerDisplayName = CleanSectionTitle(owner.DisplayName),
            FolderId = folderId,
            FolderName = folder?.Name,
            ParentFolderId = folder?.ParentFolderId,
            CanWrite = canWrite,
            DeletesPermanently = isAdmin,
            CanViewTrash = canViewTrash,
            CanClearTrash = isAdmin,
            SearchQuery = search,
            Folders = folders,
            Files = files
        });
    }

    [HttpGet("sections/{ownerUserId:guid}/trash")]
    public async Task<ActionResult<DocumentationTrashResponse>> ListTrash(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var access = await EnsureCanViewTrashAsync(ownerUserId, cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var owner = access.Owner!;
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var retentionDays = await _settings.GetDocumentationTrashRetentionDaysAsync(cancellationToken);

        // Все папки раздела (в т.ч. удалённые) — для PathLabel и «верхнего уровня» корзины.
        var allFolders = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId)
            .Select(x => new { x.Id, x.Name, x.ParentFolderId, x.DeletedAt, x.CreatedAt })
            .ToListAsync(cancellationToken);
        var byId = allFolders.ToDictionary(x => x.Id);

        string PathOf(Guid? id)
        {
            if (id is null)
            {
                return "Корень";
            }

            var parts = new List<string>();
            var guard = 0;
            var cur = id;
            while (cur is Guid cid && byId.TryGetValue(cid, out var node) && guard++ < 64)
            {
                parts.Add(node.Name);
                cur = node.ParentFolderId;
            }

            parts.Reverse();
            return string.Join(" / ", parts);
        }

        var items = new List<DocumentationTrashItemResponse>();

        foreach (var folder in allFolders.Where(x => x.DeletedAt is not null))
        {
            // В корзине показываем только корень удалённого поддерева.
            if (folder.ParentFolderId is Guid pid
                && byId.TryGetValue(pid, out var parent)
                && parent.DeletedAt is not null)
            {
                continue;
            }

            items.Add(new DocumentationTrashItemResponse
            {
                Id = folder.Id,
                IsFolder = true,
                Name = folder.Name,
                SizeBytes = null,
                DeletedAt = folder.DeletedAt!.Value,
                PathLabel = PathOf(folder.ParentFolderId)
            });
        }

        var deletedFiles = await _db.DocumentationFiles.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.DeletedAt != null)
            .OrderByDescending(x => x.DeletedAt)
            .Select(x => new { x.Id, x.FileName, x.SizeBytes, x.FolderId, x.DeletedAt })
            .ToListAsync(cancellationToken);

        foreach (var file in deletedFiles)
        {
            // Файл, удалённый вместе с папкой, в списке корзины не дублируем.
            if (file.FolderId is Guid fid
                && byId.TryGetValue(fid, out var parentFolder)
                && parentFolder.DeletedAt is not null)
            {
                continue;
            }

            items.Add(new DocumentationTrashItemResponse
            {
                Id = file.Id,
                IsFolder = false,
                Name = file.FileName,
                SizeBytes = file.SizeBytes,
                DeletedAt = file.DeletedAt!.Value,
                PathLabel = PathOf(file.FolderId)
            });
        }

        items = items
            .OrderByDescending(x => x.DeletedAt)
            .ThenBy(x => x.Name)
            .ToList();

        return Ok(new DocumentationTrashResponse
        {
            OwnerUserId = ownerUserId,
            OwnerDisplayName = CleanSectionTitle(owner.DisplayName),
            CanClearTrash = isAdmin,
            RetentionDays = retentionDays,
            Items = items
        });
    }

    [HttpPost("sections/{ownerUserId:guid}/trash/restore-folder/{folderId:guid}")]
    public async Task<IActionResult> RestoreFolder(
        Guid ownerUserId,
        Guid folderId,
        CancellationToken cancellationToken)
    {
        var access = await EnsureCanViewTrashAsync(ownerUserId, cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        // Восстанавливать может только владелец раздела (не диспетчер «чужой» корзины).
        // Админ — тоже, чтобы помочь.
        var me = GetUserId();
        if (me != ownerUserId && !User.IsInRole(AppRoles.Administrator))
        {
            return Forbid();
        }

        var folder = await _db.DocumentationFolders
            .SingleOrDefaultAsync(
                x => x.Id == folderId && x.OwnerUserId == ownerUserId && x.DeletedAt != null,
                cancellationToken);
        if (folder is null)
        {
            return NotFound();
        }

        // Если родитель тоже в корзине — поднимаем в корень, иначе конфликт/невидимость.
        if (folder.ParentFolderId is Guid parentId)
        {
            var parent = await _db.DocumentationFolders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == parentId, cancellationToken);
            if (parent is null || parent.DeletedAt is not null)
            {
                folder.ParentFolderId = null;
            }
        }

        folder.Name = await EnsureUniqueFolderNameAsync(
            ownerUserId,
            folder.ParentFolderId,
            folder.Name,
            folder.Id,
            cancellationToken);

        var treeIds = await CollectFolderTreeIdsAsync(folder.Id, includeDeleted: true, cancellationToken);

        var folders = await _db.DocumentationFolders
            .Where(x => treeIds.Contains(x.Id) && x.DeletedAt != null)
            .ToListAsync(cancellationToken);
        foreach (var f in folders)
        {
            f.DeletedAt = null;
            f.DeletedByUserId = null;
        }

        var files = await _db.DocumentationFiles
            .Where(x => x.FolderId != null
                        && treeIds.Contains(x.FolderId.Value)
                        && x.DeletedAt != null)
            .ToListAsync(cancellationToken);
        foreach (var file in files)
        {
            file.DeletedAt = null;
            file.DeletedByUserId = null;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("sections/{ownerUserId:guid}/trash/restore-file/{fileId:guid}")]
    public async Task<IActionResult> RestoreFile(
        Guid ownerUserId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var access = await EnsureCanViewTrashAsync(ownerUserId, cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var me = GetUserId();
        if (me != ownerUserId && !User.IsInRole(AppRoles.Administrator))
        {
            return Forbid();
        }

        var file = await _db.DocumentationFiles
            .SingleOrDefaultAsync(
                x => x.Id == fileId && x.OwnerUserId == ownerUserId && x.DeletedAt != null,
                cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        if (file.FolderId is Guid folderId)
        {
            var folder = await _db.DocumentationFolders.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == folderId, cancellationToken);
            if (folder is null || folder.DeletedAt is not null)
            {
                // Родитель в корзине/удалён — файл в корень раздела.
                file.FolderId = null;
            }
        }

        file.DeletedAt = null;
        file.DeletedByUserId = null;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("sections/{ownerUserId:guid}/trash/clear")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<IActionResult> ClearTrash(
        Guid ownerUserId,
        [FromBody] ClearDocumentationTrashRequest? request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request?.Confirmation?.Trim(), "TRASH", StringComparison.Ordinal))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.TrashClearConfirmRequired));
        }

        var ownerOk = await EnsureSectionExistsAsync(ownerUserId, cancellationToken);
        if (ownerOk is not null)
        {
            return ownerOk;
        }

        await _trashPurge.PurgeSectionAsync(ownerUserId, cancellationToken);
        return NoContent();
    }

    [HttpPost("sections/{ownerUserId:guid}/folders")]
    public async Task<ActionResult<DocumentationFolderItemResponse>> CreateFolder(
        Guid ownerUserId,
        [FromBody] CreateDocumentationFolderRequest request,
        CancellationToken cancellationToken)
    {
        var writeError = await EnsureCanWriteAsync(ownerUserId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        var name = NormalizeFolderName(request.Name);
        if (name is null)
        {
            return BadRequest(ApiErrors.FromCode("invalid_folder_name"));
        }

        if (request.ParentFolderId is Guid parentId)
        {
            var parentOk = await _db.DocumentationFolders.AnyAsync(
                x => x.Id == parentId && x.OwnerUserId == ownerUserId && x.DeletedAt == null,
                cancellationToken);
            if (!parentOk)
            {
                return NotFound();
            }
        }

        var duplicate = await _db.DocumentationFolders.AnyAsync(
            x => x.OwnerUserId == ownerUserId
                 && x.ParentFolderId == request.ParentFolderId
                 && x.Name == name
                 && x.DeletedAt == null,
            cancellationToken);
        if (duplicate)
        {
            return BadRequest(ApiErrors.FromCode("folder_exists"));
        }

        var folder = new DocumentationFolder
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            ParentFolderId = request.ParentFolderId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.DocumentationFolders.Add(folder);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentationFolderItemResponse
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentFolderId = folder.ParentFolderId,
            CreatedAt = folder.CreatedAt
        });
    }

    [HttpDelete("folders/{folderId:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid folderId, CancellationToken cancellationToken)
    {
        var folder = await _db.DocumentationFolders
            .SingleOrDefaultAsync(x => x.Id == folderId && x.DeletedAt == null, cancellationToken);
        if (folder is null)
        {
            return NotFound();
        }

        var writeError = await EnsureCanWriteAsync(folder.OwnerUserId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        if (User.IsInRole(AppRoles.Administrator))
        {
            var filePaths = await CollectFolderFilePathsAsync(
                folder.Id,
                onlyLive: true,
                cancellationToken);
            _db.DocumentationFolders.Remove(folder);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var path in filePaths)
            {
                _storage.DeleteIfExists(path);
            }

            return NoContent();
        }

        // Подразделение / диспетчер — в корзину (всё поддерево).
        var now = DateTimeOffset.UtcNow;
        var actor = GetUserId();
        var treeIds = await CollectFolderTreeIdsAsync(folder.Id, includeDeleted: false, cancellationToken);
        var folders = await _db.DocumentationFolders
            .Where(x => treeIds.Contains(x.Id) && x.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var f in folders)
        {
            f.DeletedAt = now;
            f.DeletedByUserId = actor;
        }

        var files = await _db.DocumentationFiles
            .Where(x => x.FolderId != null
                        && treeIds.Contains(x.FolderId.Value)
                        && x.DeletedAt == null)
            .ToListAsync(cancellationToken);
        foreach (var file in files)
        {
            file.DeletedAt = now;
            file.DeletedByUserId = actor;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("sections/{ownerUserId:guid}/files")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<DocumentationFileItemResponse>> UploadFile(
        Guid ownerUserId,
        [FromQuery] Guid? folderId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var writeError = await EnsureCanWriteAsync(ownerUserId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        if (folderId is Guid fid)
        {
            var folderOk = await _db.DocumentationFolders.AnyAsync(
                x => x.Id == fid && x.OwnerUserId == ownerUserId && x.DeletedAt == null,
                cancellationToken);
            if (!folderOk)
            {
                return NotFound();
            }
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "file_required", message = "Файл не передан." });
        }

        if (file.Length > _storage.MaxFileBytes)
        {
            return BadRequest(new { error = "file_too_large", message = "Файл слишком большой." });
        }

        await using var stream = file.OpenReadStream();
        var (relative, size) = await _storage.SaveDocumentationAsync(
            ownerUserId,
            folderId,
            file.FileName,
            stream,
            cancellationToken);

        var entity = new DocumentationFile
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            FolderId = folderId,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            SizeBytes = size,
            StoragePath = relative,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedByUserId = GetUserId()
        };
        _db.DocumentationFiles.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentationFileItemResponse
        {
            Id = entity.Id,
            FileName = entity.FileName,
            ContentType = entity.ContentType,
            SizeBytes = entity.SizeBytes,
            FolderId = entity.FolderId,
            UploadedAt = entity.UploadedAt
        });
    }

    [HttpGet("files/{fileId:guid}")]
    public async Task<IActionResult> DownloadFile(
        Guid fileId,
        [FromQuery] bool inline = false,
        CancellationToken cancellationToken = default)
    {
        var file = await LoadReadableFileAsync(fileId, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        var path = _storage.ResolveExistingPath(file.StoragePath);
        if (path is null)
        {
            return NotFound();
        }

        if (inline)
        {
            return PhysicalFile(path, file.ContentType);
        }

        return PhysicalFile(path, file.ContentType, file.FileName);
    }

    [HttpGet("files/{fileId:guid}/preview")]
    public async Task<ActionResult<AttachmentPreviewResponse>> PreviewFile(
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var file = await LoadReadableFileAsync(fileId, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        var path = _storage.ResolveExistingPath(file.StoragePath);
        if (path is null)
        {
            return NotFound();
        }

        await using var stream = global::System.IO.File.OpenRead(path);
        var preview = _preview.Build(file.FileName, file.ContentType, stream);
        return Ok(preview);
    }

    [HttpDelete("files/{fileId:guid}")]
    public async Task<IActionResult> DeleteFile(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _db.DocumentationFiles
            .SingleOrDefaultAsync(x => x.Id == fileId && x.DeletedAt == null, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        var writeError = await EnsureCanWriteAsync(file.OwnerUserId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        if (User.IsInRole(AppRoles.Administrator))
        {
            var storagePath = file.StoragePath;
            _db.DocumentationFiles.Remove(file);
            await _db.SaveChangesAsync(cancellationToken);
            _storage.DeleteIfExists(storagePath);
            return NoContent();
        }

        file.DeletedAt = DateTimeOffset.UtcNow;
        file.DeletedByUserId = GetUserId();
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("admin/users")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<List<DocumentationAdminUserResponse>>> AdminListUsers(
        CancellationToken cancellationToken)
    {
        var users = await _users.Users.AsNoTracking()
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Select(x => new DocumentationAdminUserResponse
            {
                UserId = x.Id,
                DisplayName = x.DisplayName,
                UserName = x.UserName ?? string.Empty,
                DocumentationEnabled = x.DocumentationEnabled
            })
            .ToListAsync(cancellationToken);

        foreach (var u in users)
        {
            if (string.IsNullOrWhiteSpace(u.DisplayName))
            {
                u.DisplayName = u.UserName;
            }
        }

        return Ok(users);
    }

    [HttpPut("admin/users")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<IActionResult> AdminUpdateUsers(
        [FromBody] UpdateDocumentationSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Users.Count == 0)
        {
            return NoContent();
        }

        var ids = request.Users.Select(x => x.UserId).Distinct().ToList();
        var users = await _users.Users
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var map = request.Users.ToDictionary(x => x.UserId, x => x.DocumentationEnabled);
        foreach (var user in users)
        {
            if (map.TryGetValue(user.Id, out var enabled))
            {
                user.DocumentationEnabled = enabled;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<DocumentationFile?> LoadReadableFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _db.DocumentationFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == fileId && x.DeletedAt == null, cancellationToken);
        if (file is null)
        {
            return null;
        }

        // Читать можно, если раздел владельца (Department) включён.
        var owner = await _users.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == file.OwnerUserId, cancellationToken);
        if (owner is null || !owner.IsActive || !owner.DocumentationEnabled)
        {
            return null;
        }

        if (!await _users.IsInRoleAsync(owner, AppRoles.Department))
        {
            return null;
        }

        return file;
    }

    private async Task<ActionResult?> EnsureCanWriteAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var me = GetUserId();
        if (me is null)
        {
            return Forbid();
        }

        // Админ и диспетчер пишут во все разделы; подразделение — только в свой.
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var isDispatcher = User.IsInRole(AppRoles.Dispatcher);
        if (!isAdmin && !isDispatcher && me != ownerUserId)
        {
            return Forbid();
        }

        return await EnsureSectionExistsAsync(ownerUserId, cancellationToken);
    }

    private async Task<(ApplicationUser? Owner, ActionResult? Error)> EnsureCanViewTrashAsync(
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        var me = GetUserId();
        if (me is null)
        {
            return (null, Forbid());
        }

        // Корзина видна только самому подразделению и админу (для очистки).
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        if (!isAdmin && me != ownerUserId)
        {
            return (null, Forbid());
        }

        var owner = await _users.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == ownerUserId, cancellationToken);
        if (owner is null || !owner.IsActive || !owner.DocumentationEnabled)
        {
            return (null, NotFound());
        }

        if (!await _users.IsInRoleAsync(owner, AppRoles.Department))
        {
            return (null, NotFound());
        }

        return (owner, null);
    }

    private async Task<ActionResult?> EnsureSectionExistsAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var owner = await _users.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == ownerUserId, cancellationToken);
        if (owner is null || !owner.IsActive || !owner.DocumentationEnabled)
        {
            return NotFound();
        }

        if (!await _users.IsInRoleAsync(owner, AppRoles.Department))
        {
            return Forbid();
        }

        return null;
    }

    private async Task<string> EnsureUniqueFolderNameAsync(
        Guid ownerUserId,
        Guid? parentFolderId,
        string desiredName,
        Guid selfId,
        CancellationToken cancellationToken)
    {
        var name = desiredName;
        var suffix = 2;
        while (await _db.DocumentationFolders.AnyAsync(
                   x => x.OwnerUserId == ownerUserId
                        && x.ParentFolderId == parentFolderId
                        && x.Name == name
                        && x.DeletedAt == null
                        && x.Id != selfId,
                   cancellationToken))
        {
            var candidate = $"{desiredName} ({suffix})";
            if (candidate.Length > 200)
            {
                candidate = candidate[..200];
            }

            name = candidate;
            suffix++;
            if (suffix > 1000)
            {
                break;
            }
        }

        return name;
    }

    private static string EscapeLike(string raw) =>
        raw.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    /// <summary>Без логина и хвоста « / пользователь».</summary>
    private static string CleanSectionTitle(string? displayName)
    {
        var name = (displayName ?? string.Empty).Trim();
        const string userSuffix = " / пользователь";
        if (name.EndsWith(userSuffix, StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^userSuffix.Length].Trim();
        }

        var slashIdx = name.LastIndexOf(" / ", StringComparison.Ordinal);
        if (slashIdx > 0)
        {
            var tail = name[(slashIdx + 3)..].Trim();
            // хвост похож на логин (без пробелов) — отрезаем
            if (tail.Length > 0 && !tail.Contains(' ', StringComparison.Ordinal))
            {
                name = name[..slashIdx].Trim();
            }
        }

        return string.IsNullOrWhiteSpace(name) ? "Подразделение" : name;
    }

    private async Task<List<Guid>> CollectFolderTreeIdsAsync(
        Guid rootFolderId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var result = new List<Guid> { rootFolderId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var childrenQuery = _db.DocumentationFolders.AsNoTracking()
                .Where(x => x.ParentFolderId == id);
            if (!includeDeleted)
            {
                childrenQuery = childrenQuery.Where(x => x.DeletedAt == null);
            }

            var children = await childrenQuery.Select(x => x.Id).ToListAsync(cancellationToken);
            foreach (var child in children)
            {
                result.Add(child);
                queue.Enqueue(child);
            }
        }

        return result;
    }

    private async Task<List<string>> CollectFolderFilePathsAsync(
        Guid rootFolderId,
        bool onlyLive,
        CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var filesQuery = _db.DocumentationFiles.AsNoTracking().Where(x => x.FolderId == id);
            if (onlyLive)
            {
                filesQuery = filesQuery.Where(x => x.DeletedAt == null);
            }

            var paths = await filesQuery.Select(x => x.StoragePath).ToListAsync(cancellationToken);
            result.AddRange(paths);

            var childrenQuery = _db.DocumentationFolders.AsNoTracking()
                .Where(x => x.ParentFolderId == id);
            if (onlyLive)
            {
                childrenQuery = childrenQuery.Where(x => x.DeletedAt == null);
            }

            var children = await childrenQuery.Select(x => x.Id).ToListAsync(cancellationToken);
            foreach (var child in children)
            {
                queue.Enqueue(child);
            }
        }

        return result;
    }

    private static string? NormalizeFolderName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        if (name.Length is 0 or > 200)
        {
            return null;
        }

        if (name.Contains('/') || name.Contains('\\') || name.Contains('\0')
            || name is "." or "..")
        {
            return null;
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            if (name.Contains(c))
            {
                return null;
            }
        }

        return name;
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
