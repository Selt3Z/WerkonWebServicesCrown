using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.SpecialVehicles;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

namespace Crown.Features.SpecialVehicles;

[ApiController]
[Route("api/special-vehicles")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class SpecialVehiclesController : ControllerBase
{
    private readonly AppDbContext _db;

    public SpecialVehiclesController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<SpecialVehicleUnitResponse>>> GetList(
        [FromQuery] string? search,
        [FromQuery] string? typeName,
        [FromQuery] bool freeOnly = false,
        [FromQuery] bool activeOnly = true,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        var query = _db.SpecialVehicleUnits.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(x => x.IsActive);
        }

        if (freeOnly)
        {
            query = query.Where(x => x.Availability == SpecialVehicleAvailability.Free);
        }

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            var type = typeName.Trim();
            query = query.Where(x => x.Name == type);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x =>
                x.Name.Contains(term)
                || (x.PlateOrNote != null && x.PlateOrNote.Contains(term)));
        }

        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);
        var response = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.PlateOrNote)
            .Select(x => Map(x))
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

    /// <summary>Свободные единицы + агрегат по типу для формы заявки.</summary>
    [HttpGet("availability")]
    public async Task<ActionResult<object>> GetAvailability(CancellationToken cancellationToken)
    {
        var units = await _db.SpecialVehicleUnits.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ThenBy(x => x.PlateOrNote)
            .ToListAsync(cancellationToken);

        var byType = units
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new SpecialVehicleTypeAvailabilityResponse
            {
                Name = g.First().Name,
                FreeCount = g.Count(x => x.Availability == SpecialVehicleAvailability.Free),
                TotalActive = g.Count()
            })
            .OrderBy(x => x.Name)
            .ToList();

        return Ok(new
        {
            types = byType,
            freeUnits = units
                .Where(x => x.Availability == SpecialVehicleAvailability.Free)
                .Select(Map)
                .ToList()
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SpecialVehicleUnitResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var unit = await _db.SpecialVehicleUnits.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return unit is null ? NotFound() : Ok(Map(unit));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<ActionResult<SpecialVehicleUnitResponse>> Create(
        [FromBody] SaveSpecialVehicleUnitRequest request,
        CancellationToken cancellationToken)
    {
        var (entity, error) = Build(null, request);
        if (error is not null)
        {
            return BadRequest(new { error = "validation", message = error });
        }

        _db.SpecialVehicleUnits.Add(entity!);
        await _db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = entity!.Id }, Map(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<ActionResult<SpecialVehicleUnitResponse>> Update(
        Guid id,
        [FromBody] SaveSpecialVehicleUnitRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _db.SpecialVehicleUnits
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (!request.IsActive
            && existing.Availability is SpecialVehicleAvailability.Reserved or SpecialVehicleAvailability.InUse)
        {
            return BadRequest(new { error = "in_use", message = "Нельзя отключить занятую спецтехнику." });
        }

        var (entity, error) = Build(existing, request);
        if (error is not null)
        {
            return BadRequest(new { error = "validation", message = error });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Ok(Map(entity!));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var existing = await _db.SpecialVehicleUnits
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        if (existing.Availability is SpecialVehicleAvailability.Reserved or SpecialVehicleAvailability.InUse)
        {
            return BadRequest(new { error = "in_use", message = "Нельзя удалить занятую спецтехнику." });
        }

        _db.SpecialVehicleUnits.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private static (SpecialVehicleUnit? Entity, string? Error) Build(
        SpecialVehicleUnit? existing,
        SaveSpecialVehicleUnitRequest request)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return (null, "Укажите тип/название.");
        }

        var entity = existing ?? new SpecialVehicleUnit
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTime.UtcNow,
            Availability = SpecialVehicleAvailability.Free
        };

        entity.Name = name;
        entity.PlateOrNote = string.IsNullOrWhiteSpace(request.PlateOrNote) ? null : request.PlateOrNote.Trim();
        entity.IsActive = request.IsActive;
        return (entity, null);
    }

    private static SpecialVehicleUnitResponse Map(SpecialVehicleUnit x) => new()
    {
        Id = x.Id,
        Name = x.Name,
        PlateOrNote = x.PlateOrNote,
        Availability = x.Availability,
        ReservedByRequestId = x.ReservedByRequestId,
        IsActive = x.IsActive,
        CreatedAtUtc = x.CreatedAtUtc
    };
}
