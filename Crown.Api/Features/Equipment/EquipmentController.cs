using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.Equipment;
using Crown.Contracts.Import;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Import;
using Crown.Infrastructure.Persistence;

namespace Crown.Features.Equipment;

[ApiController]
[Route("api/equipment")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class EquipmentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;
    private readonly EquipmentAssetImportService _import;

    public EquipmentController(
        AppDbContext db,
        UserManager<ApplicationUser> users,
        EquipmentAssetImportService import)
    {
        _db = db;
        _users = users;
        _import = import;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<EquipmentAssetResponse>>> GetList(
        [FromQuery] string? search,
        [FromQuery] bool freeOnly = false,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        var query = includeArchived
            ? _db.EquipmentAssets.IgnoreQueryFilters().AsNoTracking()
            : _db.EquipmentAssets.AsNoTracking();

        if (freeOnly)
        {
            query = query.Where(x => x.Availability == EquipmentAvailability.Free && !x.IsArchived);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Name.Contains(term)
                || x.InventoryNumber.Contains(term)
                || (x.Category != null && x.Category.Contains(term))
                || (x.CurrentLocationLabel != null && x.CurrentLocationLabel.Contains(term))
                || (x.OwnerLabel != null && x.OwnerLabel.Contains(term)));
        }

        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);
        var response = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.InventoryNumber)
            .Select(x => Map(x))
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

    /// <summary>Активные подразделения — выбор «текущего местоположения».</summary>
    [HttpGet("location-options")]
    public async Task<ActionResult<List<EquipmentLocationOptionResponse>>> GetLocationOptions()
    {
        var users = await _users.GetUsersInRoleAsync(AppRoles.Department);
        var items = users
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Select(x => new EquipmentLocationOptionResponse
            {
                Id = x.Id,
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName)
                    ? (x.UserName ?? string.Empty)
                    : x.DisplayName
            })
            .ToList();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<EquipmentAssetDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var asset = await _db.EquipmentAssets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Movements.OrderByDescending(m => m.OccurredAt))
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (asset is null)
        {
            return NotFound();
        }

        return Ok(new EquipmentAssetDetailResponse
        {
            Id = asset.Id,
            Name = asset.Name,
            InventoryNumber = asset.InventoryNumber,
            Category = asset.Category,
            Description = asset.Description,
            CurrentLocationUserId = asset.CurrentLocationUserId,
            CurrentLocationLabel = asset.CurrentLocationLabel,
            OwnerUserId = asset.OwnerUserId,
            OwnerLabel = asset.OwnerLabel,
            Availability = asset.Availability,
            ReservedByRequestId = asset.ReservedByRequestId,
            IsArchived = asset.IsArchived,
            CreatedAtUtc = asset.CreatedAtUtc,
            Movements = asset.Movements.Select(m => new EquipmentMovementResponse
            {
                Id = m.Id,
                OccurredAt = m.OccurredAt,
                FromLocationUserId = m.FromLocationUserId,
                FromLocationLabel = m.FromLocationLabel,
                ToLocationUserId = m.ToLocationUserId,
                ToLocationLabel = m.ToLocationLabel,
                ServiceRequestId = m.ServiceRequestId,
                Note = m.Note,
                ByUserDisplayName = m.ByUserDisplayName
            }).ToList()
        });
    }

    [HttpPost]
    public async Task<ActionResult<EquipmentAssetResponse>> Create(
        [FromBody] SaveEquipmentAssetRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanWriteEquipmentAsync(cancellationToken))
        {
            return Forbid();
        }

        var (entity, error) = await BuildAsync(null, request, cancellationToken);
        if (error is not null)
        {
            return BadRequest(new { error = "validation", message = error });
        }

        _db.EquipmentAssets.Add(entity!);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = entity!.Id }, Map(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<EquipmentAssetResponse>> Update(
        Guid id,
        [FromBody] SaveEquipmentAssetRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanWriteEquipmentAsync(cancellationToken))
        {
            return Forbid();
        }

        var existing = await _db.EquipmentAssets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var (entity, error) = await BuildAsync(existing, request, cancellationToken);
        if (error is not null)
        {
            return BadRequest(new { error = "validation", message = error });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(Map(entity!));
    }

    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<EquipmentAssetResponse>> Archive(
        Guid id,
        [FromQuery] bool archive = true,
        CancellationToken cancellationToken = default)
    {
        if (!await CanWriteEquipmentAsync(cancellationToken))
        {
            return Forbid();
        }

        var entity = await _db.EquipmentAssets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (archive && entity.Availability == EquipmentAvailability.Reserved)
        {
            return BadRequest(new { error = "reserved", message = "Нельзя архивировать забронированную единицу." });
        }

        entity.IsArchived = archive;
        entity.ArchivedAtUtc = archive ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(Map(entity));
    }

    [HttpPost("import/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SpreadsheetImportFileHelper.MaxFileBytes + 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportPreviewResponse>> PreviewImport(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!await CanWriteEquipmentAsync(cancellationToken))
        {
            return Forbid();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.SpreadsheetImportInvalidFile));
        }

        if (file.Length > SpreadsheetImportFileHelper.MaxFileBytes)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.SpreadsheetImportFileTooLarge));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            return Ok(_import.Preview(buffer.ToArray(), file.FileName));
        }
        catch (SpreadsheetImportException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.Code));
        }
    }

    [HttpPost("import/commit")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportCommitResponse>> CommitImport(
        [FromBody] SpreadsheetImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanWriteEquipmentAsync(cancellationToken))
        {
            return Forbid();
        }

        try
        {
            return Ok(await _import.CommitAsync(request, cancellationToken));
        }
        catch (SpreadsheetImportException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.Code));
        }
    }

    private async Task<bool> CanWriteEquipmentAsync(CancellationToken cancellationToken)
    {
        if (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher))
        {
            return true;
        }

        var user = await _users.GetUserAsync(User);
        return user is { IsActive: true, CanManageEquipment: true };
    }

    private async Task<(EquipmentAsset? Entity, string? Error)> BuildAsync(
        EquipmentAsset? existing,
        SaveEquipmentAssetRequest request,
        CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        var inv = (request.InventoryNumber ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "Укажите название.");
        }

        if (string.IsNullOrWhiteSpace(inv))
        {
            return (null, "Укажите инвентарный номер.");
        }

        var duplicate = await _db.EquipmentAssets
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.InventoryNumber == inv && (existing == null || x.Id != existing.Id),
                cancellationToken);
        if (duplicate)
        {
            return (null, "Инвентарный номер уже занят.");
        }

        var (locationUserId, locationLabel, locationError) =
            await ResolveDepartmentAsync(request.CurrentLocationUserId, "текущее местоположение", cancellationToken);
        if (locationError is not null)
        {
            return (null, locationError);
        }

        // «Принадлежит»: задаётся при создании; менять могут только админ/диспетчер.
        var canChangeOwner = User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);
        Guid resolvedOwnerId;
        string ownerLabel;

        if (existing is not null && !canChangeOwner)
        {
            if (existing.OwnerUserId is null)
            {
                return (null, "У единицы не задан владелец — обратитесь к диспетчеру.");
            }

            resolvedOwnerId = existing.OwnerUserId.Value;
            ownerLabel = existing.OwnerLabel ?? string.Empty;
        }
        else
        {
            var ownerUserId = request.OwnerUserId;
            if ((ownerUserId is null || ownerUserId == Guid.Empty) && existing is null && !canChangeOwner)
            {
                var me = await _users.GetUserAsync(User);
                if (me is { IsActive: true } && await _users.IsInRoleAsync(me, AppRoles.Department))
                {
                    ownerUserId = me.Id;
                }
            }

            var ownerResolved = await ResolveDepartmentAsync(ownerUserId, "принадлежность", cancellationToken);
            if (ownerResolved.Error is not null)
            {
                return (null, ownerResolved.Error);
            }

            resolvedOwnerId = ownerResolved.Id;
            ownerLabel = ownerResolved.Label;
        }

        var entity = existing ?? new EquipmentAsset
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            Availability = EquipmentAvailability.Free
        };

        entity.Name = name;
        entity.InventoryNumber = inv;
        entity.Category = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim();
        entity.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        entity.CurrentLocationUserId = locationUserId;
        entity.CurrentLocationLabel = locationLabel;
        entity.OwnerUserId = resolvedOwnerId;
        entity.OwnerLabel = ownerLabel;
        return (entity, null);
    }

    private async Task<(Guid Id, string Label, string? Error)> ResolveDepartmentAsync(
        Guid? userId,
        string fieldTitle,
        CancellationToken cancellationToken)
    {
        if (userId is null || userId == Guid.Empty)
        {
            return (Guid.Empty, string.Empty, $"Укажите {fieldTitle} (подразделение).");
        }

        var user = await _users.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);
        if (user is null)
        {
            return (Guid.Empty, string.Empty, $"Подразделение для поля «{fieldTitle}» не найдено.");
        }

        if (!await _users.IsInRoleAsync(user, AppRoles.Department))
        {
            return (Guid.Empty, string.Empty, $"«{fieldTitle}» — только пользователь с ролью «Подразделение».");
        }

        var label = string.IsNullOrWhiteSpace(user.DisplayName)
            ? (user.UserName ?? string.Empty)
            : user.DisplayName.Trim();
        return (user.Id, label, null);
    }

    private static EquipmentAssetResponse Map(EquipmentAsset x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        InventoryNumber = x.InventoryNumber,
        Category = x.Category,
        Description = x.Description,
        CurrentLocationUserId = x.CurrentLocationUserId,
        CurrentLocationLabel = x.CurrentLocationLabel,
        OwnerUserId = x.OwnerUserId,
        OwnerLabel = x.OwnerLabel,
        Availability = x.Availability,
        ReservedByRequestId = x.ReservedByRequestId,
        IsArchived = x.IsArchived,
        CreatedAtUtc = x.CreatedAtUtc
    };
}
