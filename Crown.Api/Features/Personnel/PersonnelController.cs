using System.Security.Claims;
using Crown.Contracts.Common;
using Crown.Contracts.Personnel;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crown.Features.Personnel;

/// <summary>
/// Персонал подразделений (мастера и т.п.; per-user OwnerUserId, без логина).
/// Подразделение ведёт свой список; админ/диспетчер — чужие (своего нет).
/// </summary>
[ApiController]
[Route("api/personnel")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class PersonnelController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public PersonnelController(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    private bool IsStaff =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    [HttpGet]
    public async Task<ActionResult<PagedResponse<PersonnelPersonResponse>>> GetAll(
        [FromQuery] Guid? ownerUserId,
        [FromQuery] string? search,
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
            return Ok(new PagedResponse<PersonnelPersonResponse> { Items = [], HasMore = false });
        }

        var query = _db.PersonnelPeople.AsNoTracking()
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
                x.FullName.ToLower().Contains(normalizedSearch)
                || (x.Position != null && x.Position.ToLower().Contains(normalizedSearch)));
        }

        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);
        var ownerName = await ResolveOwnerNameAsync(scopeOwnerId.Value, cancellationToken);

        var page = await query
            .OrderBy(x => x.FullName)
            .ThenBy(x => x.Id)
            .Select(x => new PersonnelPersonResponse
            {
                Id = x.Id,
                OwnerUserId = x.OwnerUserId,
                OwnerDisplayName = ownerName,
                FullName = x.FullName,
                Position = x.Position,
                IsActive = x.IsActive,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(page);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PersonnelPersonResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await _db.PersonnelPeople.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        return Ok(await MapResponseAsync(entity, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<PersonnelPersonResponse>> Create(
        SavePersonnelPersonRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { error = "full_name_required", message = "Укажите ФИО." });
        }

        if (!TryResolveOwnerForWrite(request.OwnerUserId, out var ownerId, out var ownerError))
        {
            return BadRequest(new { error = "owner_required", message = ownerError });
        }

        if (!await IsDepartmentUserAsync(ownerId))
        {
            return BadRequest(new { error = "owner_not_initiator", message = "Персонал только у пользователей с ролью «Подразделение»." });
        }

        var entity = new PersonnelPerson
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerId,
            FullName = request.FullName.Trim(),
            Position = NormalizeOptional(request.Position),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.PersonnelPeople.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, await MapResponseAsync(entity, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PersonnelPersonResponse>> Update(
        Guid id,
        SavePersonnelPersonRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return BadRequest(new { error = "full_name_required", message = "Укажите ФИО." });
        }

        var entity = await _db.PersonnelPeople.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!CanWriteOwner(entity.OwnerUserId))
        {
            return Forbid();
        }

        // Владельца записи staff может переназначить только на другое подразделение.
        if (IsStaff && request.OwnerUserId is Guid newOwner && newOwner != entity.OwnerUserId)
        {
            if (!await IsDepartmentUserAsync(newOwner))
            {
                return BadRequest(new { error = "owner_not_initiator", message = "Персонал только у подразделений." });
            }

            // Остановки старой бригады больше не должны ссылаться на человека из чужого списка.
            await _db.BrigadeStoppages
                .Where(x => x.MasterPersonId == entity.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.MasterPersonId, (Guid?)null), cancellationToken);
            await _db.BrigadeStoppages
                .Where(x => x.StoppedByPersonId == entity.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.StoppedByPersonId, (Guid?)null), cancellationToken);

            entity.OwnerUserId = newOwner;
        }

        entity.FullName = request.FullName.Trim();
        entity.Position = NormalizeOptional(request.Position);
        entity.IsActive = request.IsActive;

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(await MapResponseAsync(entity, cancellationToken));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.PersonnelPeople.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!CanWriteOwner(entity.OwnerUserId))
        {
            return Forbid();
        }

        // В остановках оставляем снимки имён, ссылки на человека убираем.
        await _db.BrigadeStoppages
            .Where(x => x.MasterPersonId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.MasterPersonId, (Guid?)null), cancellationToken);
        await _db.BrigadeStoppages
            .Where(x => x.StoppedByPersonId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StoppedByPersonId, (Guid?)null), cancellationToken);

        _db.PersonnelPeople.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Подразделение → свой список (или чужой по ownerUserId — нужно оператору рейтинга при выборе мастера).
    /// Staff → owner из query (может быть null = «ещё не выбрали»).
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
                error = "Выберите подразделение, чей персонал редактируете.";
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

        // Non-staff не может писать в чужой список.
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

    private async Task<bool> IsDepartmentUserAsync(Guid userId)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        return user is not null && await _users.IsInRoleAsync(user, AppRoles.Department);
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

    private async Task<PersonnelPersonResponse> MapResponseAsync(PersonnelPerson entity, CancellationToken cancellationToken) =>
        new()
        {
            Id = entity.Id,
            OwnerUserId = entity.OwnerUserId,
            OwnerDisplayName = await ResolveOwnerNameAsync(entity.OwnerUserId, cancellationToken),
            FullName = entity.FullName,
            Position = entity.Position,
            IsActive = entity.IsActive,
            CreatedAtUtc = entity.CreatedAtUtc
        };

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
