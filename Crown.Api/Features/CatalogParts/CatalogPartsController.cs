using System.Security.Claims;
using Crown.Contracts.CatalogParts;
using Crown.Contracts.Common;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crown.Features.CatalogParts;

/// <summary>
/// Каталог услуг подразделений (per-user OwnerUserId).
/// Инициатор ведёт свой; админ/диспетчер — чужие (своего нет).
/// </summary>
[ApiController]
[Route("api/catalog-parts")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class CatalogPartsController : ControllerBase
{
    public const string NoCategoryFilterValue = "__none__";

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public CatalogPartsController(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private bool IsStaff =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<CatalogPartResponse>>> GetAll(
        [FromQuery] Guid? ownerUserId,
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] bool activeOnly = false,
        [FromQuery] string? activeFilter = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveOwnerScope(ownerUserId, out var scopeOwnerId, out var error))
        {
            return BadRequest(new { error = "owner_required", message = error });
        }

        // Staff без выбранного владельца — пустой список (UI просит выбрать).
        if (scopeOwnerId is null)
        {
            return Ok(new PagedResponse<CatalogPartResponse> { Items = [], HasMore = false });
        }

        var query = _db.CatalogParts.AsNoTracking()
            .Where(x => x.OwnerUserId == scopeOwnerId.Value);

        var normalizedActiveFilter = (activeFilter ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedActiveFilter is "active" || (activeOnly && string.IsNullOrEmpty(normalizedActiveFilter)))
        {
            query = query.Where(x => x.IsActive);
        }
        else if (normalizedActiveFilter is "inactive")
        {
            query = query.Where(x => !x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x =>
                x.Name.ToLower().Contains(normalizedSearch)
                || (x.Code != null && x.Code.ToLower().Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim();
            if (string.Equals(normalizedCategory, NoCategoryFilterValue, StringComparison.Ordinal))
            {
                query = query.Where(x => x.Category == null || x.Category == string.Empty);
            }
            else
            {
                var categoryLower = normalizedCategory.ToLower();
                query = query.Where(x => x.Category != null && x.Category.ToLower() == categoryLower);
            }
        }

        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);
        var ownerName = await ResolveOwnerNameAsync(scopeOwnerId.Value, cancellationToken);

        var page = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new CatalogPartResponse
            {
                Id = x.Id,
                OwnerUserId = x.OwnerUserId,
                OwnerDisplayName = ownerName,
                Name = x.Name,
                Code = x.Code,
                Category = x.Category,
                DefaultUnitPrice = x.DefaultUnitPrice,
                DefaultUnit = x.DefaultUnit,
                Description = x.Description,
                SlaHours = x.SlaHours,
                IsActive = x.IsActive,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(page);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetCategories(
        [FromQuery] Guid? ownerUserId,
        CancellationToken cancellationToken)
    {
        if (!TryResolveOwnerScope(ownerUserId, out var scopeOwnerId, out var error))
        {
            return BadRequest(new { error = "owner_required", message = error });
        }

        if (scopeOwnerId is null)
        {
            return Ok(Array.Empty<string>());
        }

        var categories = await _db.CatalogParts
            .AsNoTracking()
            .Where(x => x.OwnerUserId == scopeOwnerId.Value
                        && x.Category != null
                        && x.Category != string.Empty)
            .Select(x => x.Category!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CatalogPartResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await _db.CatalogParts.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        // Чтение чужой позиции — ок (выбор услуги при создании заявки).
        return Ok(await MapResponseAsync(entity, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<CatalogPartResponse>> Create(
        SaveCatalogPartRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        if (!TryResolveOwnerForWrite(request.OwnerUserId, out var ownerId, out var ownerError))
        {
            return BadRequest(new { error = "owner_required", message = ownerError });
        }

        if (!await IsInitiatorUserAsync(ownerId))
        {
            return BadRequest(new { error = "owner_not_initiator", message = "Каталог только у пользователей с ролью «Инициатор»." });
        }

        var entity = new CatalogPart
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            Name = request.Name.Trim(),
            Code = NormalizeOptional(request.Code),
            Category = NormalizeOptional(request.Category),
            DefaultUnitPrice = request.DefaultUnitPrice < 0 ? 0 : request.DefaultUnitPrice,
            DefaultUnit = string.IsNullOrWhiteSpace(request.DefaultUnit) ? "шт" : request.DefaultUnit.Trim(),
            Description = NormalizeOptional(request.Description),
            SlaHours = NormalizeSla(request.SlaHours),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.CatalogParts.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, await MapResponseAsync(entity, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CatalogPartResponse>> Update(
        Guid id,
        SaveCatalogPartRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        var entity = await _db.CatalogParts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!CanWriteOwner(entity.OwnerUserId))
        {
            return Forbid();
        }

        // Владельца позиции staff может переназначить только на другого инициатора.
        if (IsStaff && request.OwnerUserId is Guid newOwner && newOwner != entity.OwnerUserId)
        {
            if (!await IsInitiatorUserAsync(newOwner))
            {
                return BadRequest(new { error = "owner_not_initiator", message = "Каталог только у инициаторов." });
            }

            entity.OwnerUserId = newOwner;
        }

        entity.Name = request.Name.Trim();
        entity.Code = NormalizeOptional(request.Code);
        entity.Category = NormalizeOptional(request.Category);
        // Цену в UI не трогаем — сохраняем текущую, если с клиента пришёл 0 и это не явный апдейт.
        if (request.DefaultUnitPrice > 0 || entity.DefaultUnitPrice == 0)
        {
            entity.DefaultUnitPrice = request.DefaultUnitPrice < 0 ? 0 : request.DefaultUnitPrice;
        }

        entity.DefaultUnit = string.IsNullOrWhiteSpace(request.DefaultUnit) ? entity.DefaultUnit : request.DefaultUnit.Trim();
        entity.Description = NormalizeOptional(request.Description);
        entity.SlaHours = NormalizeSla(request.SlaHours);
        entity.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(await MapResponseAsync(entity, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.CatalogParts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!CanWriteOwner(entity.OwnerUserId))
        {
            return Forbid();
        }

        _db.CatalogParts.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Инициатор → только свой owner.
    /// Staff → owner из query (может быть null = «ещё не выбрали»).
    /// Чтение чужого активного каталога (форма заявки) — любой BusinessData по ownerUserId инициатора.
    /// </summary>
    private bool TryResolveOwnerScope(Guid? requestedOwnerId, out Guid? scopeOwnerId, out string? error)
    {
        error = null;
        scopeOwnerId = null;
        var selfId = GetUserId();

        if (IsStaff)
        {
            scopeOwnerId = requestedOwnerId is Guid id && id != Guid.Empty ? id : null;
            return true;
        }

        // Инициатор (и прочие non-staff): свой каталог по умолчанию.
        // Для заявки можно запросить каталог исполнителя через ownerUserId.
        if (requestedOwnerId is Guid other && other != Guid.Empty && other != selfId)
        {
            scopeOwnerId = other;
            return true;
        }

        if (selfId is null)
        {
            error = "Не удалось определить пользователя.";
            return false;
        }

        scopeOwnerId = selfId;
        return true;
    }

    private bool TryResolveOwnerForWrite(Guid? requestedOwnerId, out Guid ownerId, out string? error)
    {
        error = null;
        ownerId = Guid.Empty;
        var selfId = GetUserId();

        if (IsStaff)
        {
            if (requestedOwnerId is not Guid id || id == Guid.Empty)
            {
                error = "Выберите подразделение (инициатора), чей каталог редактируете.";
                return false;
            }

            ownerId = id;
            return true;
        }

        if (selfId is null)
        {
            error = "Не удалось определить пользователя.";
            return false;
        }

        // Non-staff не может писать в чужой каталог.
        ownerId = selfId.Value;
        return true;
    }

    private bool CanWriteOwner(Guid ownerUserId)
    {
        if (IsStaff)
        {
            return true;
        }

        var selfId = GetUserId();
        return selfId is not null && selfId == ownerUserId;
    }

    private async Task<bool> IsInitiatorUserAsync(Guid userId)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        return user is not null && await _users.IsInRoleAsync(user, AppRoles.Initiator);
    }

    private async Task<string> ResolveOwnerNameAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(x => x.Id == ownerUserId)
            .Select(x => new { x.DisplayName, x.UserName })
            .FirstOrDefaultAsync(cancellationToken);
        if (user is null)
        {
            return ownerUserId.ToString();
        }

        return string.IsNullOrWhiteSpace(user.DisplayName)
            ? (user.UserName ?? ownerUserId.ToString())
            : user.DisplayName!;
    }

    private async Task<CatalogPartResponse> MapResponseAsync(CatalogPart entity, CancellationToken cancellationToken) =>
        new()
        {
            Id = entity.Id,
            OwnerUserId = entity.OwnerUserId,
            OwnerDisplayName = await ResolveOwnerNameAsync(entity.OwnerUserId, cancellationToken),
            Name = entity.Name,
            Code = entity.Code,
            Category = entity.Category,
            DefaultUnitPrice = entity.DefaultUnitPrice,
            DefaultUnit = entity.DefaultUnit,
            Description = entity.Description,
            SlaHours = entity.SlaHours,
            IsActive = entity.IsActive,
            CreatedAtUtc = entity.CreatedAtUtc
        };

    private static string? ValidateRequest(SaveCatalogPartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiErrorCodes.ServiceNameRequired;
        }

        if (!string.IsNullOrWhiteSpace(request.DefaultUnit) && !MeasurementUnits.IsAllowed(request.DefaultUnit))
        {
            return ApiErrorCodes.UnsupportedMeasurementUnit;
        }

        if (request.DefaultUnitPrice < 0)
        {
            return ApiErrorCodes.DefaultUnitPriceNegative;
        }

        return null;
    }

    private static int? NormalizeSla(int? hours) =>
        hours is null or <= 0 ? null : hours;

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
