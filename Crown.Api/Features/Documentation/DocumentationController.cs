using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Documentation;
using Crown.Contracts.Requests;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Attachments;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

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

    public DocumentationController(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        AttachmentStorageService storage,
        AttachmentPreviewService preview)
    {
        _db = db;
        _users = users;
        _storage = storage;
        _preview = preview;
    }

    /// <summary>Подразделы — только роль Department с включённой документацией.</summary>
    [HttpGet("sections")]
    public async Task<ActionResult<List<DocumentationSectionResponse>>> ListSections(
        CancellationToken cancellationToken)
    {
        var me = GetUserId();
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        // Только роль Department; админы/диспетчеры в список разделов не попадают.
        var departmentUsers = await _users.GetUsersInRoleAsync(AppRoles.Department);
        var owners = departmentUsers
            .Where(x => x.IsActive && x.DocumentationEnabled)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .ToList();

        var ownerIds = owners.Select(x => x.Id).ToList();
        var stats = await _db.DocumentationFiles.AsNoTracking()
            .Where(f => ownerIds.Contains(f.OwnerUserId))
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
                CanWrite = isAdmin || me == x.Id,
                FileCount = st?.FileCount ?? 0,
                TotalSizeBytes = st?.TotalSizeBytes ?? 0
            };
        }).ToList());
    }

    [HttpGet("sections/{ownerUserId:guid}")]
    public async Task<ActionResult<DocumentationBrowseResponse>> Browse(
        Guid ownerUserId,
        [FromQuery] Guid? folderId,
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
                .SingleOrDefaultAsync(x => x.Id == fid && x.OwnerUserId == ownerUserId, cancellationToken);
            if (folder is null)
            {
                return NotFound();
            }
        }

        var folders = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.ParentFolderId == folderId)
            .OrderBy(x => x.Name)
            .Select(x => new DocumentationFolderItemResponse
            {
                Id = x.Id,
                Name = x.Name,
                ParentFolderId = x.ParentFolderId,
                CreatedAt = x.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var files = await _db.DocumentationFiles.AsNoTracking()
            .Where(x => x.OwnerUserId == ownerUserId && x.FolderId == folderId)
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

        var me = GetUserId();
        var canWrite = User.IsInRole(AppRoles.Administrator) || me == ownerUserId;
        return Ok(new DocumentationBrowseResponse
        {
            OwnerUserId = ownerUserId,
            OwnerDisplayName = CleanSectionTitle(owner.DisplayName),
            FolderId = folderId,
            FolderName = folder?.Name,
            ParentFolderId = folder?.ParentFolderId,
            CanWrite = canWrite,
            Folders = folders,
            Files = files
        });
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
                x => x.Id == parentId && x.OwnerUserId == ownerUserId,
                cancellationToken);
            if (!parentOk)
            {
                return NotFound();
            }
        }

        var duplicate = await _db.DocumentationFolders.AnyAsync(
            x => x.OwnerUserId == ownerUserId
                 && x.ParentFolderId == request.ParentFolderId
                 && x.Name == name,
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
            .SingleOrDefaultAsync(x => x.Id == folderId, cancellationToken);
        if (folder is null)
        {
            return NotFound();
        }

        var writeError = await EnsureCanWriteAsync(folder.OwnerUserId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        // Каскад в БД; файлы на диске — вручную по дереву.
        var filePaths = await CollectFolderFilePathsAsync(folder.Id, cancellationToken);
        _db.DocumentationFolders.Remove(folder);
        await _db.SaveChangesAsync(cancellationToken);
        foreach (var path in filePaths)
        {
            _storage.DeleteIfExists(path);
        }

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
                x => x.Id == fid && x.OwnerUserId == ownerUserId,
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
            .SingleOrDefaultAsync(x => x.Id == fileId, cancellationToken);
        if (file is null)
        {
            return NotFound();
        }

        var writeError = await EnsureCanWriteAsync(file.OwnerUserId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        var storagePath = file.StoragePath;
        _db.DocumentationFiles.Remove(file);
        await _db.SaveChangesAsync(cancellationToken);
        _storage.DeleteIfExists(storagePath);
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
            .SingleOrDefaultAsync(x => x.Id == fileId, cancellationToken);
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

        // Админ пишет во все разделы подразделений; остальные — только в свой.
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        if (!isAdmin && me != ownerUserId)
        {
            return Forbid();
        }

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

    private async Task<List<string>> CollectFolderFilePathsAsync(Guid rootFolderId, CancellationToken cancellationToken)
    {
        var result = new List<string>();
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var paths = await _db.DocumentationFiles.AsNoTracking()
                .Where(x => x.FolderId == id)
                .Select(x => x.StoragePath)
                .ToListAsync(cancellationToken);
            result.AddRange(paths);

            var children = await _db.DocumentationFolders.AsNoTracking()
                .Where(x => x.ParentFolderId == id)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
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
