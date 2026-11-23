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
    private readonly Crown.Infrastructure.Documentation.DocumentationInboxSyncService _inboxSync;

    public DocumentationController(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        AttachmentStorageService storage,
        AttachmentPreviewService preview,
        AppSettingsService settings,
        Crown.Infrastructure.Documentation.DocumentationTrashPurgeService trashPurge,
        Crown.Infrastructure.Documentation.DocumentationInboxSyncService inboxSync)
    {
        _db = db;
        _users = users;
        _storage = storage;
        _preview = preview;
        _settings = settings;
        _trashPurge = trashPurge;
        _inboxSync = inboxSync;
    }

    /// <summary>Каталоги: staff — все активные; остальные — DocumentationEnabled + членство.</summary>
    [HttpGet("libraries")]
    public async Task<ActionResult<List<DocumentationLibraryResponse>>> ListLibraries(
        CancellationToken cancellationToken)
    {
        var access = await ResolveCallerAccessAsync(cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        if (!access.IsStaff && !access.DocumentationEnabled)
        {
            return Ok(new List<DocumentationLibraryResponse>());
        }

        var me = access.UserId!.Value;
        var libraries = await _db.DocumentationLibraries.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, DocumentationLibraryMember>? membership = null;
        if (!access.IsStaff)
        {
            membership = await _db.DocumentationLibraryMembers.AsNoTracking()
                .Where(x => x.UserId == me)
                .ToDictionaryAsync(x => x.LibraryId, cancellationToken);
            libraries = libraries.Where(x => membership.ContainsKey(x.Id)).ToList();
        }

        var libraryIds = libraries.Select(x => x.Id).ToList();
        var stats = await _db.DocumentationFiles.AsNoTracking()
            .Where(f => f.LibraryId != null
                        && libraryIds.Contains(f.LibraryId.Value)
                        && f.DeletedAt == null)
            .GroupBy(f => f.LibraryId!.Value)
            .Select(g => new { LibraryId = g.Key, FileCount = g.Count(), TotalSizeBytes = g.Sum(x => x.SizeBytes) })
            .ToDictionaryAsync(x => x.LibraryId, cancellationToken);

        var isAdmin = access.IsAdmin;
        return Ok(libraries.Select(lib =>
        {
            stats.TryGetValue(lib.Id, out var st);
            var member = membership?.GetValueOrDefault(lib.Id);
            var canWrite = access.IsStaff || (member?.CanWrite ?? false);
            return new DocumentationLibraryResponse
            {
                Id = lib.Id,
                Name = lib.Name,
                CanWrite = canWrite,
                CanManageMembers = isAdmin,
                FileCount = st?.FileCount ?? 0,
                TotalSizeBytes = st?.TotalSizeBytes ?? 0,
                SortOrder = lib.SortOrder,
                IsActive = lib.IsActive,
                InboxFolderKey = lib.InboxFolderKey
            };
        }).ToList());
    }

    [HttpPost("libraries")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<DocumentationLibraryResponse>> CreateLibrary(
        [FromBody] CreateDocumentationLibraryRequest request,
        CancellationToken cancellationToken)
    {
        var name = NormalizeLibraryName(request.Name);
        if (name is null)
        {
            return BadRequest(ApiErrors.FromCode("invalid_library_name"));
        }

        var inboxKey = NormalizeInboxKey(request.InboxFolderKey);
        var library = new DocumentationLibrary
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = request.SortOrder,
            IsActive = true,
            InboxFolderKey = inboxKey,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GetUserId()
        };
        _db.DocumentationLibraries.Add(library);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentationLibraryResponse
        {
            Id = library.Id,
            Name = library.Name,
            CanWrite = true,
            CanManageMembers = true,
            SortOrder = library.SortOrder,
            IsActive = library.IsActive,
            InboxFolderKey = library.InboxFolderKey
        });
    }

    [HttpPut("libraries/{libraryId:guid}")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<DocumentationLibraryResponse>> UpdateLibrary(
        Guid libraryId,
        [FromBody] UpdateDocumentationLibraryRequest request,
        CancellationToken cancellationToken)
    {
        var library = await _db.DocumentationLibraries
            .SingleOrDefaultAsync(x => x.Id == libraryId, cancellationToken);
        if (library is null)
        {
            return NotFound();
        }

        var name = NormalizeLibraryName(request.Name);
        if (name is null)
        {
            return BadRequest(ApiErrors.FromCode("invalid_library_name"));
        }

        library.Name = name;
        library.SortOrder = request.SortOrder;
        library.IsActive = request.IsActive;
        library.InboxFolderKey = NormalizeInboxKey(request.InboxFolderKey);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentationLibraryResponse
        {
            Id = library.Id,
            Name = library.Name,
            CanWrite = true,
            CanManageMembers = true,
            SortOrder = library.SortOrder,
            IsActive = library.IsActive,
            InboxFolderKey = library.InboxFolderKey
        });
    }

    [HttpDelete("libraries/{libraryId:guid}")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<IActionResult> DeleteLibrary(Guid libraryId, CancellationToken cancellationToken)
    {
        var library = await _db.DocumentationLibraries
            .SingleOrDefaultAsync(x => x.Id == libraryId, cancellationToken);
        if (library is null)
        {
            return NotFound();
        }

        // Мягкое отключение: каталог скрывается, файлы остаются.
        library.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("libraries/{libraryId:guid}/members")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<List<DocumentationLibraryMemberResponse>>> ListMembers(
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        if (!await _db.DocumentationLibraries.AnyAsync(x => x.Id == libraryId, cancellationToken))
        {
            return NotFound();
        }

        var members = await _db.DocumentationLibraryMembers.AsNoTracking()
            .Where(x => x.LibraryId == libraryId)
            .ToListAsync(cancellationToken);
        var userIds = members.Select(x => x.UserId).ToList();
        var users = await _users.Users.AsNoTracking()
            .Where(x => userIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        return Ok(members
            .Select(m =>
            {
                users.TryGetValue(m.UserId, out var u);
                return new DocumentationLibraryMemberResponse
                {
                    UserId = m.UserId,
                    DisplayName = u is null
                        ? "—"
                        : UserDisplay.Format(u.DisplayName, u.UserName, u.BrigadeNumber),
                    UserName = u?.UserName ?? string.Empty,
                    CanWrite = m.CanWrite
                };
            })
            .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    [HttpPut("libraries/{libraryId:guid}/members")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<IActionResult> UpdateMembers(
        Guid libraryId,
        [FromBody] UpdateLibraryMembersRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _db.DocumentationLibraries.AnyAsync(x => x.Id == libraryId, cancellationToken))
        {
            return NotFound();
        }

        var existing = await _db.DocumentationLibraryMembers
            .Where(x => x.LibraryId == libraryId)
            .ToListAsync(cancellationToken);
        _db.DocumentationLibraryMembers.RemoveRange(existing);

        var now = DateTimeOffset.UtcNow;
        var seen = new HashSet<Guid>();
        foreach (var item in request.Members)
        {
            if (!seen.Add(item.UserId))
            {
                continue;
            }

            _db.DocumentationLibraryMembers.Add(new DocumentationLibraryMember
            {
                Id = Guid.NewGuid(),
                LibraryId = libraryId,
                UserId = item.UserId,
                CanWrite = item.CanWrite,
                CreatedAt = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>Владельцы старых разделов без LibraryId (для one-shot переноса).</summary>
    [HttpGet("admin/legacy-sources")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<DocumentationLegacySourcesResponse>> ListLegacySources(
        CancellationToken cancellationToken)
    {
        var folderOwners = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.LibraryId == null && x.OwnerUserId != null)
            .GroupBy(x => x.OwnerUserId!.Value)
            .Select(g => new { OwnerUserId = g.Key, FolderCount = g.Count() })
            .ToListAsync(cancellationToken);

        var fileOwners = await _db.DocumentationFiles.AsNoTracking()
            .Where(x => x.LibraryId == null && x.OwnerUserId != null)
            .GroupBy(x => x.OwnerUserId!.Value)
            .Select(g => new { OwnerUserId = g.Key, FileCount = g.Count() })
            .ToListAsync(cancellationToken);

        var ownerIds = folderOwners.Select(x => x.OwnerUserId)
            .Concat(fileOwners.Select(x => x.OwnerUserId))
            .Distinct()
            .ToList();

        var users = await _users.Users.AsNoTracking()
            .Where(x => ownerIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var folderMap = folderOwners.ToDictionary(x => x.OwnerUserId, x => x.FolderCount);
        var fileMap = fileOwners.ToDictionary(x => x.OwnerUserId, x => x.FileCount);

        var items = ownerIds.Select(id =>
        {
            users.TryGetValue(id, out var u);
            return new DocumentationLegacySourceItem
            {
                OwnerUserId = id,
                DisplayName = u is null
                    ? id.ToString("N")
                    : UserDisplay.Format(u.DisplayName, u.UserName, u.BrigadeNumber),
                UserName = u?.UserName ?? string.Empty,
                FolderCount = folderMap.GetValueOrDefault(id),
                FileCount = fileMap.GetValueOrDefault(id)
            };
        })
        .OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

        return Ok(new DocumentationLegacySourcesResponse { Items = items });
    }

    [HttpPost("admin/migrate")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<MigrateDocumentationResponse>> Migrate(
        [FromBody] MigrateDocumentationRequest request,
        CancellationToken cancellationToken)
    {
        var response = new MigrateDocumentationResponse();
        if (request.Items.Count == 0)
        {
            return Ok(response);
        }

        foreach (var item in request.Items)
        {
            var library = await _db.DocumentationLibraries
                .SingleOrDefaultAsync(x => x.Id == item.LibraryId, cancellationToken);
            if (library is null)
            {
                response.Messages.Add($"Каталог {item.LibraryId:N} не найден.");
                continue;
            }

            var folders = await _db.DocumentationFolders
                .Where(x => x.OwnerUserId == item.OwnerUserId && x.LibraryId == null)
                .ToListAsync(cancellationToken);
            foreach (var folder in folders)
            {
                folder.LibraryId = item.LibraryId;
            }

            var files = await _db.DocumentationFiles
                .Where(x => x.OwnerUserId == item.OwnerUserId && x.LibraryId == null)
                .ToListAsync(cancellationToken);
            foreach (var file in files)
            {
                file.LibraryId = item.LibraryId;
            }

            response.FoldersUpdated += folders.Count;
            response.FilesUpdated += files.Count;

            // Inbox key из логина владельца, если у каталога ещё нет.
            if (string.IsNullOrWhiteSpace(library.InboxFolderKey))
            {
                var owner = await _users.Users.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == item.OwnerUserId, cancellationToken);
                if (owner is not null)
                {
                    var key = Crown.Infrastructure.Documentation.DocumentationInboxSyncService
                        .ResolveFolderKey(owner);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        library.InboxFolderKey = key;
                    }
                }
            }

            var memberExists = await _db.DocumentationLibraryMembers.AnyAsync(
                x => x.LibraryId == item.LibraryId && x.UserId == item.OwnerUserId,
                cancellationToken);
            if (!memberExists)
            {
                _db.DocumentationLibraryMembers.Add(new DocumentationLibraryMember
                {
                    Id = Guid.NewGuid(),
                    LibraryId = item.LibraryId,
                    UserId = item.OwnerUserId,
                    CanWrite = true,
                    CreatedAt = DateTimeOffset.UtcNow
                });
                response.MembersAdded++;
            }

            await _db.SaveChangesAsync(cancellationToken);
            response.Messages.Add(
                $"Owner {item.OwnerUserId:N} → {library.Name}: папок {folders.Count}, файлов {files.Count}.");
        }

        return Ok(response);
    }

    /// <summary>Кандидаты для ACL (активные пользователи).</summary>
    [HttpGet("admin/users")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<List<DocumentationAclUserResponse>>> AdminListUsers(
        CancellationToken cancellationToken)
    {
        var rows = await _users.Users.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Select(x => new
            {
                x.Id,
                x.DisplayName,
                x.UserName,
                x.BrigadeNumber,
                x.IsActive
            })
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(x => new DocumentationAclUserResponse
        {
            UserId = x.Id,
            DisplayName = UserDisplay.Format(x.DisplayName, x.UserName, x.BrigadeNumber),
            UserName = x.UserName ?? string.Empty,
            IsActive = x.IsActive
        }).ToList());
    }

    [HttpGet("search")]
    public async Task<ActionResult<DocumentationGlobalSearchResponse>> GlobalSearch(
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var query = (q ?? string.Empty).Trim();
        if (query.Length < 2)
        {
            return Ok(new DocumentationGlobalSearchResponse { Query = query, Hits = [] });
        }

        var libraries = await ListAccessibleLibrariesAsync(cancellationToken);
        if (libraries.Count == 0)
        {
            return Ok(new DocumentationGlobalSearchResponse { Query = query, Hits = [] });
        }

        var libraryIds = libraries.Select(x => x.Id).ToList();
        var libraryNames = libraries.ToDictionary(x => x.Id, x => x.Name);
        var term = query.ToLowerInvariant();
        var like = $"%{EscapeLike(query)}%";

        var allFolders = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.LibraryId != null
                        && libraryIds.Contains(x.LibraryId.Value)
                        && x.DeletedAt == null)
            .Select(x => new { x.Id, x.Name, x.ParentFolderId, LibraryId = x.LibraryId!.Value, x.CreatedAt })
            .ToListAsync(cancellationToken);
        var byId = allFolders.ToDictionary(x => x.Id);

        string PathOf(Guid? id, Guid libraryId)
        {
            if (id is null)
            {
                return "Корень";
            }

            var parts = new List<string>();
            var guard = 0;
            var cur = id;
            while (cur is Guid cid && byId.TryGetValue(cid, out var node)
                   && node.LibraryId == libraryId && guard++ < 64)
            {
                parts.Add(node.Name);
                cur = node.ParentFolderId;
            }

            parts.Reverse();
            return parts.Count == 0 ? "Корень" : string.Join(" / ", parts);
        }

        var folderHits = allFolders
            .Where(x => x.Name.ToLowerInvariant().Contains(term))
            .OrderBy(x => x.Name)
            .Take(100)
            .Select(x => new DocumentationGlobalSearchHitResponse
            {
                LibraryId = x.LibraryId,
                LibraryName = libraryNames.GetValueOrDefault(x.LibraryId, "—"),
                IsFolder = true,
                Id = x.Id,
                Name = x.Name,
                PathLabel = PathOf(x.ParentFolderId, x.LibraryId),
                At = x.CreatedAt
            });

        var fileRows = await _db.DocumentationFiles.AsNoTracking()
            .Where(x => x.LibraryId != null
                        && libraryIds.Contains(x.LibraryId.Value)
                        && x.DeletedAt == null
                        && EF.Functions.ILike(x.FileName, like))
            .OrderBy(x => x.FileName)
            .Take(100)
            .Select(x => new { x.Id, x.FileName, LibraryId = x.LibraryId!.Value, x.FolderId, x.SizeBytes, x.UploadedAt })
            .ToListAsync(cancellationToken);

        var fileHits = fileRows.Select(x => new DocumentationGlobalSearchHitResponse
        {
            LibraryId = x.LibraryId,
            LibraryName = libraryNames.GetValueOrDefault(x.LibraryId, "—"),
            IsFolder = false,
            Id = x.Id,
            Name = x.FileName,
            FolderId = x.FolderId,
            PathLabel = PathOf(x.FolderId, x.LibraryId),
            SizeBytes = x.SizeBytes,
            At = x.UploadedAt
        });

        var hits = folderHits.Concat(fileHits)
            .OrderBy(x => x.LibraryName)
            .ThenBy(x => x.Name)
            .Take(150)
            .ToList();

        return Ok(new DocumentationGlobalSearchResponse { Query = query, Hits = hits });
    }

    [HttpGet("libraries/{libraryId:guid}")]
    public async Task<ActionResult<DocumentationBrowseResponse>> Browse(
        Guid libraryId,
        [FromQuery] Guid? folderId,
        [FromQuery] string? q,
        CancellationToken cancellationToken)
    {
        var read = await EnsureCanReadAsync(libraryId, cancellationToken);
        if (read.Error is not null)
        {
            return read.Error;
        }

        var library = read.Library!;
        DocumentationFolder? folder = null;
        if (folderId is Guid fid)
        {
            folder = await _db.DocumentationFolders.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == fid && x.LibraryId == libraryId && x.DeletedAt == null,
                    cancellationToken);
            if (folder is null)
            {
                return NotFound();
            }
        }

        var write = await ResolveWriteFlagsAsync(libraryId, cancellationToken);
        var search = string.IsNullOrWhiteSpace(q) ? null : q.Trim();

        List<DocumentationFolderItemResponse> folders;
        List<DocumentationFileItemResponse> files;

        if (search is not null)
        {
            var allFolders = await _db.DocumentationFolders.AsNoTracking()
                .Where(x => x.LibraryId == libraryId && x.DeletedAt == null)
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
                .Where(x => x.LibraryId == libraryId
                            && x.DeletedAt == null
                            && EF.Functions.ILike(x.FileName, like))
                .OrderBy(x => x.FileName)
                .Take(200)
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

            folders = folders.Take(200).ToList();
        }
        else
        {
            folders = await _db.DocumentationFolders.AsNoTracking()
                .Where(x => x.LibraryId == libraryId
                            && x.ParentFolderId == folderId
                            && x.DeletedAt == null)
                .OrderBy(x => x.Name)
                .Take(500)
                .Select(x => new DocumentationFolderItemResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    ParentFolderId = x.ParentFolderId,
                    CreatedAt = x.CreatedAt
                })
                .ToListAsync(cancellationToken);

            files = await _db.DocumentationFiles.AsNoTracking()
                .Where(x => x.LibraryId == libraryId
                            && x.FolderId == folderId
                            && x.DeletedAt == null)
                .OrderBy(x => x.FileName)
                .Take(500)
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
            LibraryId = libraryId,
            LibraryName = library.Name,
            FolderId = folderId,
            FolderName = folder?.Name,
            ParentFolderId = folder?.ParentFolderId,
            CanWrite = write.CanWrite,
            DeletesPermanently = write.IsAdmin,
            CanViewTrash = write.CanViewTrash,
            CanClearTrash = write.IsAdmin,
            SearchQuery = search,
            Folders = folders,
            Files = files
        });
    }

    [HttpGet("libraries/{libraryId:guid}/trash")]
    public async Task<ActionResult<DocumentationTrashResponse>> ListTrash(
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        var access = await EnsureCanViewTrashAsync(libraryId, cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var library = access.Library!;
        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var retentionDays = await _settings.GetDocumentationTrashRetentionDaysAsync(cancellationToken);

        var allFolders = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.LibraryId == libraryId)
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
            .Where(x => x.LibraryId == libraryId && x.DeletedAt != null)
            .OrderByDescending(x => x.DeletedAt)
            .Select(x => new { x.Id, x.FileName, x.SizeBytes, x.FolderId, x.DeletedAt })
            .ToListAsync(cancellationToken);

        foreach (var file in deletedFiles)
        {
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
            LibraryId = libraryId,
            LibraryName = library.Name,
            CanClearTrash = isAdmin,
            RetentionDays = retentionDays,
            Items = items
        });
    }

    [HttpPost("libraries/{libraryId:guid}/trash/restore-folder/{folderId:guid}")]
    public async Task<IActionResult> RestoreFolder(
        Guid libraryId,
        Guid folderId,
        CancellationToken cancellationToken)
    {
        var access = await EnsureCanViewTrashAsync(libraryId, cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var folder = await _db.DocumentationFolders
            .SingleOrDefaultAsync(
                x => x.Id == folderId && x.LibraryId == libraryId && x.DeletedAt != null,
                cancellationToken);
        if (folder is null)
        {
            return NotFound();
        }

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
            libraryId,
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

    [HttpPost("libraries/{libraryId:guid}/trash/restore-file/{fileId:guid}")]
    public async Task<IActionResult> RestoreFile(
        Guid libraryId,
        Guid fileId,
        CancellationToken cancellationToken)
    {
        var access = await EnsureCanViewTrashAsync(libraryId, cancellationToken);
        if (access.Error is not null)
        {
            return access.Error;
        }

        var file = await _db.DocumentationFiles
            .SingleOrDefaultAsync(
                x => x.Id == fileId && x.LibraryId == libraryId && x.DeletedAt != null,
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
                file.FolderId = null;
            }
        }

        file.DeletedAt = null;
        file.DeletedByUserId = null;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("libraries/{libraryId:guid}/trash/clear")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<IActionResult> ClearTrash(
        Guid libraryId,
        [FromBody] ClearDocumentationTrashRequest? request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request?.Confirmation?.Trim(), "TRASH", StringComparison.Ordinal))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.TrashClearConfirmRequired));
        }

        if (!await _db.DocumentationLibraries.AnyAsync(x => x.Id == libraryId, cancellationToken))
        {
            return NotFound();
        }

        await _trashPurge.PurgeLibraryAsync(libraryId, cancellationToken);
        return NoContent();
    }

    [HttpPost("libraries/{libraryId:guid}/folders")]
    public async Task<ActionResult<DocumentationFolderItemResponse>> CreateFolder(
        Guid libraryId,
        [FromBody] CreateDocumentationFolderRequest request,
        CancellationToken cancellationToken)
    {
        var writeError = await EnsureCanWriteAsync(libraryId, cancellationToken);
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
                x => x.Id == parentId && x.LibraryId == libraryId && x.DeletedAt == null,
                cancellationToken);
            if (!parentOk)
            {
                return NotFound();
            }
        }

        var duplicate = await _db.DocumentationFolders.AnyAsync(
            x => x.LibraryId == libraryId
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
            LibraryId = libraryId,
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

    [HttpGet("libraries/{libraryId:guid}/folders")]
    public async Task<ActionResult<List<DocumentationFolderItemResponse>>> ListAllFolders(
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        var read = await EnsureCanReadAsync(libraryId, cancellationToken);
        if (read.Error is not null)
        {
            return read.Error;
        }

        var folders = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.LibraryId == libraryId && x.DeletedAt == null)
            .Select(x => new { x.Id, x.Name, x.ParentFolderId, x.CreatedAt })
            .ToListAsync(cancellationToken);

        var byId = folders.ToDictionary(x => x.Id);
        string PathOf(Guid? parentId)
        {
            if (parentId is null)
            {
                return "Корень";
            }

            var parts = new List<string>();
            var guard = 0;
            var cur = parentId;
            while (cur is Guid id && byId.TryGetValue(id, out var node) && guard++ < 64)
            {
                parts.Add(node.Name);
                cur = node.ParentFolderId;
            }

            parts.Reverse();
            return parts.Count == 0 ? "Корень" : string.Join(" / ", parts);
        }

        return Ok(folders
            .Select(x =>
            {
                var parentPath = PathOf(x.ParentFolderId);
                return new DocumentationFolderItemResponse
                {
                    Id = x.Id,
                    Name = x.Name,
                    ParentFolderId = x.ParentFolderId,
                    CreatedAt = x.CreatedAt,
                    PathLabel = parentPath == "Корень" ? x.Name : $"{parentPath} / {x.Name}"
                };
            })
            .OrderBy(x => x.PathLabel, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    [HttpPost("folders/{folderId:guid}/move")]
    public async Task<ActionResult<DocumentationFolderItemResponse>> MoveFolder(
        Guid folderId,
        [FromBody] MoveDocumentationFolderRequest request,
        CancellationToken cancellationToken)
    {
        var folder = await _db.DocumentationFolders
            .SingleOrDefaultAsync(x => x.Id == folderId && x.DeletedAt == null, cancellationToken);
        if (folder is null || folder.LibraryId is null)
        {
            return NotFound();
        }

        var libraryId = folder.LibraryId.Value;
        var writeError = await EnsureCanWriteAsync(libraryId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        var targetParentId = request.TargetParentFolderId;
        if (targetParentId == folder.Id)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.InvalidFolderMove));
        }

        if (targetParentId is Guid tid)
        {
            var target = await _db.DocumentationFolders.AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Id == tid && x.LibraryId == libraryId && x.DeletedAt == null,
                    cancellationToken);
            if (target is null)
            {
                return NotFound();
            }

            var descendants = await CollectFolderTreeIdsAsync(folder.Id, includeDeleted: false, cancellationToken);
            if (descendants.Contains(tid))
            {
                return BadRequest(ApiErrors.FromCode(ApiErrorCodes.InvalidFolderMove));
            }
        }

        if (folder.ParentFolderId == targetParentId)
        {
            return Ok(new DocumentationFolderItemResponse
            {
                Id = folder.Id,
                Name = folder.Name,
                ParentFolderId = folder.ParentFolderId,
                CreatedAt = folder.CreatedAt
            });
        }

        folder.Name = await EnsureUniqueFolderNameAsync(
            libraryId,
            targetParentId,
            folder.Name,
            folder.Id,
            cancellationToken);
        folder.ParentFolderId = targetParentId;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentationFolderItemResponse
        {
            Id = folder.Id,
            Name = folder.Name,
            ParentFolderId = folder.ParentFolderId,
            CreatedAt = folder.CreatedAt
        });
    }

    [HttpPost("files/{fileId:guid}/move")]
    public async Task<ActionResult<DocumentationFileItemResponse>> MoveFile(
        Guid fileId,
        [FromBody] MoveDocumentationFileRequest request,
        CancellationToken cancellationToken)
    {
        var file = await _db.DocumentationFiles
            .SingleOrDefaultAsync(x => x.Id == fileId && x.DeletedAt == null, cancellationToken);
        if (file is null || file.LibraryId is null)
        {
            return NotFound();
        }

        var libraryId = file.LibraryId.Value;
        var writeError = await EnsureCanWriteAsync(libraryId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        var targetFolderId = request.TargetFolderId;
        if (targetFolderId is Guid fid)
        {
            var folderOk = await _db.DocumentationFolders.AnyAsync(
                x => x.Id == fid && x.LibraryId == libraryId && x.DeletedAt == null,
                cancellationToken);
            if (!folderOk)
            {
                return NotFound();
            }
        }

        file.FolderId = targetFolderId;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new DocumentationFileItemResponse
        {
            Id = file.Id,
            FileName = file.FileName,
            ContentType = file.ContentType,
            SizeBytes = file.SizeBytes,
            FolderId = file.FolderId,
            UploadedAt = file.UploadedAt
        });
    }

    [HttpDelete("folders/{folderId:guid}")]
    public async Task<IActionResult> DeleteFolder(Guid folderId, CancellationToken cancellationToken)
    {
        var folder = await _db.DocumentationFolders
            .SingleOrDefaultAsync(x => x.Id == folderId && x.DeletedAt == null, cancellationToken);
        if (folder is null || folder.LibraryId is null)
        {
            return NotFound();
        }

        var writeError = await EnsureCanWriteAsync(folder.LibraryId.Value, cancellationToken);
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

    [HttpGet("libraries/{libraryId:guid}/inbox")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<DocumentationInboxInfoResponse>> GetInboxInfo(
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        var library = await _db.DocumentationLibraries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == libraryId, cancellationToken);
        if (library is null)
        {
            return NotFound();
        }

        var info = _inboxSync.GetInboxInfo(library);
        return Ok(new DocumentationInboxInfoResponse
        {
            LibraryId = info.LibraryId,
            FolderKey = info.FolderKey,
            HostInboxPath = info.HostInboxPath,
            RelativeInboxPath = info.RelativeInboxPath,
            PendingFileCount = info.PendingFileCount,
            MaxInboxFileBytes = info.MaxInboxFileBytes
        });
    }

    [HttpPost("libraries/{libraryId:guid}/sync-inbox")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<DocumentationInboxSyncResponse>> SyncInbox(
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        if (!await _db.DocumentationLibraries.AnyAsync(x => x.Id == libraryId, cancellationToken))
        {
            return NotFound();
        }

        var result = await _inboxSync.SyncLibraryAsync(libraryId, GetUserId(), cancellationToken);
        return Ok(ToInboxSyncResponse(result));
    }

    [HttpPost("sync-inbox")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<DocumentationInboxSyncResponse>> SyncAllInbox(
        CancellationToken cancellationToken)
    {
        var result = await _inboxSync.SyncAllAsync(cancellationToken);
        return Ok(ToInboxSyncResponse(result));
    }

    private static DocumentationInboxSyncResponse ToInboxSyncResponse(
        Crown.Infrastructure.Documentation.DocumentationInboxSyncResult result) =>
        new()
        {
            LibraryId = result.LibraryId,
            FolderKey = result.FolderKey,
            HostInboxPath = result.HostInboxPath,
            Imported = result.Imported,
            Updated = result.Updated,
            Skipped = result.Skipped,
            Failed = result.Failed,
            FoldersCreated = result.FoldersCreated,
            PendingBefore = result.PendingBefore,
            Messages = result.Messages
        };

    [HttpPost("libraries/{libraryId:guid}/files")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<DocumentationFileItemResponse>> UploadFile(
        Guid libraryId,
        [FromQuery] Guid? folderId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var writeError = await EnsureCanWriteAsync(libraryId, cancellationToken);
        if (writeError is not null)
        {
            return writeError;
        }

        if (folderId is Guid fid)
        {
            var folderOk = await _db.DocumentationFolders.AnyAsync(
                x => x.Id == fid && x.LibraryId == libraryId && x.DeletedAt == null,
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
            libraryId,
            folderId,
            file.FileName,
            stream,
            cancellationToken);

        var entity = new DocumentationFile
        {
            Id = Guid.NewGuid(),
            LibraryId = libraryId,
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
        if (file is null || file.LibraryId is null)
        {
            return NotFound();
        }

        var writeError = await EnsureCanWriteAsync(file.LibraryId.Value, cancellationToken);
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

    private async Task<DocumentationFile?> LoadReadableFileAsync(Guid fileId, CancellationToken cancellationToken)
    {
        var file = await _db.DocumentationFiles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == fileId && x.DeletedAt == null, cancellationToken);
        if (file is null || file.LibraryId is null)
        {
            return null;
        }

        var read = await EnsureCanReadAsync(file.LibraryId.Value, cancellationToken);
        return read.Error is null ? file : null;
    }

    private async Task<ActionResult?> EnsureCanWriteAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var flags = await ResolveWriteFlagsAsync(libraryId, cancellationToken);
        if (flags.Library is null)
        {
            return NotFound();
        }

        if (!flags.CanWrite)
        {
            return Forbid();
        }

        return null;
    }

    private async Task<(DocumentationLibrary? Library, ActionResult? Error)> EnsureCanReadAsync(
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        var access = await ResolveCallerAccessAsync(cancellationToken);
        if (access.Error is not null)
        {
            return (null, access.Error);
        }

        var library = await _db.DocumentationLibraries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == libraryId && x.IsActive, cancellationToken);
        if (library is null)
        {
            return (null, NotFound());
        }

        if (access.IsStaff)
        {
            return (library, null);
        }

        if (!access.DocumentationEnabled)
        {
            return (null, Forbid());
        }

        var isMember = await _db.DocumentationLibraryMembers.AsNoTracking()
            .AnyAsync(x => x.LibraryId == libraryId && x.UserId == access.UserId, cancellationToken);
        if (!isMember)
        {
            return (null, Forbid());
        }

        return (library, null);
    }

    private async Task<(DocumentationLibrary? Library, ActionResult? Error)> EnsureCanViewTrashAsync(
        Guid libraryId,
        CancellationToken cancellationToken)
    {
        var flags = await ResolveWriteFlagsAsync(libraryId, cancellationToken);
        if (flags.Library is null)
        {
            return (null, NotFound());
        }

        // Корзина: админ или участник с CanWrite.
        if (!flags.CanViewTrash)
        {
            return (null, Forbid());
        }

        return (flags.Library, null);
    }

    private async Task<(
        DocumentationLibrary? Library,
        bool CanWrite,
        bool CanViewTrash,
        bool IsAdmin)> ResolveWriteFlagsAsync(Guid libraryId, CancellationToken cancellationToken)
    {
        var access = await ResolveCallerAccessAsync(cancellationToken);
        if (access.UserId is null)
        {
            return (null, false, false, false);
        }

        var library = await _db.DocumentationLibraries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == libraryId && x.IsActive, cancellationToken);
        if (library is null)
        {
            return (null, false, false, false);
        }

        if (access.IsAdmin)
        {
            return (library, true, true, true);
        }

        if (access.IsDispatcher)
        {
            return (library, true, false, false);
        }

        if (!access.DocumentationEnabled)
        {
            return (library, false, false, false);
        }

        var member = await _db.DocumentationLibraryMembers.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.LibraryId == libraryId && x.UserId == access.UserId,
                cancellationToken);
        var canWrite = member?.CanWrite ?? false;
        return (library, canWrite, canWrite, false);
    }

    private async Task<(
        Guid? UserId,
        bool IsAdmin,
        bool IsDispatcher,
        bool IsStaff,
        bool DocumentationEnabled,
        ActionResult? Error)> ResolveCallerAccessAsync(CancellationToken cancellationToken)
    {
        var me = GetUserId();
        if (me is null)
        {
            return (null, false, false, false, false, Forbid());
        }

        var isAdmin = User.IsInRole(AppRoles.Administrator);
        var isDispatcher = User.IsInRole(AppRoles.Dispatcher);
        if (isAdmin || isDispatcher)
        {
            return (me, isAdmin, isDispatcher, true, true, null);
        }

        var user = await _users.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == me, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return (me, false, false, false, false, Forbid());
        }

        return (me, false, false, false, user.DocumentationEnabled, null);
    }

    private async Task<List<DocumentationLibrary>> ListAccessibleLibrariesAsync(
        CancellationToken cancellationToken)
    {
        var access = await ResolveCallerAccessAsync(cancellationToken);
        if (access.UserId is null)
        {
            return [];
        }

        if (access.IsStaff)
        {
            return await _db.DocumentationLibraries.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.Name)
                .ToListAsync(cancellationToken);
        }

        if (!access.DocumentationEnabled)
        {
            return [];
        }

        var memberIds = await _db.DocumentationLibraryMembers.AsNoTracking()
            .Where(x => x.UserId == access.UserId)
            .Select(x => x.LibraryId)
            .ToListAsync(cancellationToken);

        return await _db.DocumentationLibraries.AsNoTracking()
            .Where(x => x.IsActive && memberIds.Contains(x.Id))
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    private async Task<string> EnsureUniqueFolderNameAsync(
        Guid libraryId,
        Guid? parentFolderId,
        string desiredName,
        Guid selfId,
        CancellationToken cancellationToken)
    {
        var name = desiredName;
        var suffix = 2;
        while (await _db.DocumentationFolders.AnyAsync(
                   x => x.LibraryId == libraryId
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

    private async Task<List<Guid>> CollectFolderTreeIdsAsync(
        Guid rootFolderId,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        var root = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.Id == rootFolderId)
            .Select(x => new { x.Id, x.LibraryId })
            .FirstOrDefaultAsync(cancellationToken);
        if (root is null || root.LibraryId is null)
        {
            return [];
        }

        var all = await _db.DocumentationFolders.AsNoTracking()
            .Where(x => x.LibraryId == root.LibraryId
                        && (includeDeleted || x.DeletedAt == null))
            .Select(x => new { x.Id, x.ParentFolderId })
            .ToListAsync(cancellationToken);

        var childrenOf = all
            .Where(x => x.ParentFolderId is not null)
            .GroupBy(x => x.ParentFolderId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

        var result = new List<Guid> { rootFolderId };
        var queue = new Queue<Guid>();
        queue.Enqueue(rootFolderId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!childrenOf.TryGetValue(id, out var children))
            {
                continue;
            }

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

    private static string? NormalizeLibraryName(string? raw)
    {
        var name = (raw ?? string.Empty).Trim();
        return name.Length is 0 or > 200 ? null : name;
    }

    private static string? NormalizeInboxKey(string? raw)
    {
        var key = AttachmentStorageService.SanitizeFolderSegment(raw ?? string.Empty);
        if (string.IsNullOrWhiteSpace(key)
            || string.Equals(key, "unknown", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return key.Length > 100 ? key[..100] : key;
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
