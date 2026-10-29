using System.Security.Claims;
using Crown.Contracts.Transport;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crown.Features.Transport;

[ApiController]
[Route("api/transport")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class TransportController : ControllerBase
{
    private static readonly VehicleTripStatus[] OpenStatuses =
    [
        VehicleTripStatus.Reserved,
        VehicleTripStatus.Active
    ];

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public TransportController(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    [HttpGet("board")]
    public async Task<ActionResult<TransportBoardResponse>> GetBoard(
        [FromQuery] Guid? parkingId,
        [FromQuery] string? search,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var vehicles = await _db.Vehicles.AsNoTracking()
            .Include(x => x.CurrentParking)
            .Where(x => x.IsActive)
            .OrderBy(x => x.PlateNumber)
            .ToListAsync(cancellationToken);

        if (parkingId is Guid pid)
        {
            vehicles = vehicles
                .Where(x => x.CurrentParkingId == pid)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            vehicles = vehicles
                .Where(x => x.PlateNumber.ToLowerInvariant().Contains(term)
                            || x.Name.ToLowerInvariant().Contains(term)
                            || (x.VehicleType?.ToLowerInvariant().Contains(term) ?? false)
                            || (x.Vin?.ToLowerInvariant().Contains(term) ?? false))
                .ToList();
        }

        var openTrips = await _db.VehicleTrips.AsNoTracking()
            .Where(x => OpenStatuses.Contains(x.Status))
            .ToListAsync(cancellationToken);
        var openByVehicle = openTrips
            .GroupBy(x => x.VehicleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.PlannedStartAt).First());

        var free = new List<VehicleResponse>();
        var busy = new List<VehicleResponse>();
        foreach (var v in vehicles)
        {
            openByVehicle.TryGetValue(v.Id, out var trip);
            var item = MapVehicle(v, trip, now);
            if (item.Availability == "free")
            {
                free.Add(item);
            }
            else
            {
                busy.Add(item);
            }
        }

        busy = busy
            .OrderByDescending(x => x.IsOverdue)
            .ThenBy(x => x.ActivePlannedEndAt)
            .ToList();

        var parkings = await _db.ParkingLots.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new ParkingLotResponse
            {
                Id = x.Id,
                Name = x.Name,
                Address = x.Address,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);

        var recentTrips = await _db.VehicleTrips.AsNoTracking()
            .Where(t => t.Status == VehicleTripStatus.Returned)
            .OrderByDescending(t => t.ActualEndAt)
            .Take(15)
            .ToListAsync(cancellationToken);
        var recentVehicleIds = recentTrips.Select(t => t.VehicleId).Distinct().ToList();
        var recentParkingIds = recentTrips
            .Where(t => t.ReturnParkingId is not null)
            .Select(t => t.ReturnParkingId!.Value)
            .Distinct()
            .ToList();
        var recentVehicles = await _db.Vehicles.AsNoTracking()
            .Where(v => recentVehicleIds.Contains(v.Id))
            .ToDictionaryAsync(v => v.Id, cancellationToken);
        var recentParkings = await _db.ParkingLots.AsNoTracking()
            .Where(p => recentParkingIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var recent = recentTrips
            .Select(t => MapTrip(
                t,
                recentVehicles.GetValueOrDefault(t.VehicleId),
                t.ReturnParkingId is Guid rpid ? recentParkings.GetValueOrDefault(rpid) : null,
                now))
            .ToList();

        var borrowers = await LoadBorrowersAsync();

        return Ok(new TransportBoardResponse
        {
            Free = free,
            Busy = busy,
            Parkings = parkings,
            RecentReturns = recent,
            Borrowers = borrowers
        });
    }

    [HttpGet("parkings")]
    public async Task<ActionResult<List<ParkingLotResponse>>> GetParkings(CancellationToken cancellationToken)
    {
        var items = await _db.ParkingLots.AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new ParkingLotResponse
            {
                Id = x.Id,
                Name = x.Name,
                Address = x.Address,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost("parkings")]
    [Authorize(Policy = AuthorizationPolicies.ManageBusinessRecords)]
    public async Task<ActionResult<ParkingLotResponse>> CreateParking(
        [FromBody] SaveParkingLotRequest request,
        CancellationToken cancellationToken)
    {
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { error = "name_required", message = "Укажите название стоянки." });
        }

        var entity = new ParkingLot
        {
            Id = Guid.NewGuid(),
            Name = name,
            Address = NullIfEmpty(request.Address),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.ParkingLots.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(new ParkingLotResponse
        {
            Id = entity.Id,
            Name = entity.Name,
            Address = entity.Address,
            IsActive = entity.IsActive
        });
    }

    [HttpPost("vehicles")]
    [Authorize(Policy = AuthorizationPolicies.ManageBusinessRecords)]
    public async Task<ActionResult<VehicleResponse>> CreateVehicle(
        [FromBody] SaveVehicleRequest request,
        CancellationToken cancellationToken)
    {
        var plate = NormalizePlate(request.PlateNumber);
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(plate) || string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { error = "required", message = "Укажите госномер и название." });
        }

        if (await _db.Vehicles.AnyAsync(x => x.PlateNumber == plate, cancellationToken))
        {
            return Conflict(new { error = "plate_exists", message = "Машина с таким номером уже есть." });
        }

        if (request.CurrentParkingId is Guid parkingId
            && !await _db.ParkingLots.AnyAsync(x => x.Id == parkingId && x.IsActive, cancellationToken))
        {
            return BadRequest(new { error = "parking_not_found", message = "Стоянка не найдена." });
        }

        var entity = new Vehicle
        {
            Id = Guid.NewGuid(),
            PlateNumber = plate,
            Name = name,
            VehicleType = NullIfEmpty(request.VehicleType),
            Vin = NormalizeVin(request.Vin),
            IsInRepair = request.IsInRepair,
            CurrentParkingId = request.CurrentParkingId,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Vehicles.Add(entity);
        if (entity.IsInRepair)
        {
            OpenRepair(entity.Id);
        }

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(MapVehicle(entity, trip: null, DateTimeOffset.UtcNow));
    }

    [HttpGet("vehicles/{id:guid}")]
    public async Task<ActionResult<VehicleDetailResponse>> GetVehicle(
        Guid id,
        CancellationToken cancellationToken)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .Include(x => x.CurrentParking)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        var openTrip = await _db.VehicleTrips.AsNoTracking()
            .Where(x => x.VehicleId == id && OpenStatuses.Contains(x.Status))
            .OrderByDescending(x => x.PlannedStartAt)
            .FirstOrDefaultAsync(cancellationToken);

        var refuels = await _db.VehicleFuelRefuels.AsNoTracking()
            .Where(x => x.VehicleId == id)
            .OrderByDescending(x => x.RefueledAt)
            .Take(200)
            .Select(x => new VehicleFuelRefuelResponse
            {
                Id = x.Id,
                VehicleId = x.VehicleId,
                RefueledAt = x.RefueledAt,
                Liters = x.Liters,
                FuelType = x.FuelType,
                PricePerLiter = x.PricePerLiter,
                Cost = x.Cost,
                Note = x.Note
            })
            .ToListAsync(cancellationToken);

        var tripEntities = await _db.VehicleTrips.AsNoTracking()
            .Where(x => x.VehicleId == id)
            .OrderByDescending(x => x.ActualEndAt ?? x.ActualStartAt ?? x.PlannedStartAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        var parkingIds = tripEntities
            .Where(t => t.ReturnParkingId is not null)
            .Select(t => t.ReturnParkingId!.Value)
            .Distinct()
            .ToList();
        var parkings = parkingIds.Count == 0
            ? new Dictionary<Guid, ParkingLot>()
            : await _db.ParkingLots.AsNoTracking()
                .Where(p => parkingIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, cancellationToken);
        var trips = tripEntities
            .Select(t => MapTrip(
                t,
                vehicle,
                t.ReturnParkingId is Guid rpid ? parkings.GetValueOrDefault(rpid) : null,
                now))
            .ToList();

        var repairs = await _db.VehicleRepairs.AsNoTracking()
            .Where(x => x.VehicleId == id)
            .OrderByDescending(x => x.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        return Ok(new VehicleDetailResponse
        {
            Vehicle = MapVehicle(vehicle, openTrip, now),
            Refuels = refuels,
            Trips = trips,
            Repairs = repairs.Select(r => MapRepair(r, now)).ToList(),
            TotalLiters = refuels.Sum(x => x.Liters),
            TotalCost = refuels.Sum(x => x.Cost)
        });
    }

    [HttpPut("vehicles/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageBusinessRecords)]
    public async Task<ActionResult<VehicleResponse>> UpdateVehicle(
        Guid id,
        [FromBody] SaveVehicleRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.Vehicles
            .Include(x => x.CurrentParking)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var plate = NormalizePlate(request.PlateNumber);
        var name = (request.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(plate) || string.IsNullOrWhiteSpace(name))
        {
            return BadRequest(new { error = "required", message = "Укажите госномер и название." });
        }

        if (await _db.Vehicles.AnyAsync(x => x.PlateNumber == plate && x.Id != id, cancellationToken))
        {
            return Conflict(new { error = "plate_exists", message = "Машина с таким номером уже есть." });
        }

        if (request.CurrentParkingId is Guid parkingId
            && !await _db.ParkingLots.AnyAsync(x => x.Id == parkingId && x.IsActive, cancellationToken))
        {
            return BadRequest(new { error = "parking_not_found", message = "Стоянка не найдена." });
        }

        entity.PlateNumber = plate;
        entity.Name = name;
        entity.VehicleType = NullIfEmpty(request.VehicleType);
        entity.Vin = NormalizeVin(request.Vin);
        // История ремонтов: открыть/закрыть период при смене галочки.
        if (request.IsInRepair != entity.IsInRepair)
        {
            if (request.IsInRepair)
            {
                OpenRepair(entity.Id);
            }
            else
            {
                await CloseOpenRepairAsync(entity.Id, cancellationToken);
            }
        }

        entity.IsInRepair = request.IsInRepair;
        entity.CurrentParkingId = request.CurrentParkingId;
        // «Активна» убрана из UI — машина либо в парке, либо удалена.
        entity.IsActive = true;
        await _db.SaveChangesAsync(cancellationToken);

        // Подтянуть имя стоянки после смены FK.
        if (entity.CurrentParkingId is Guid pid)
        {
            entity.CurrentParking = await _db.ParkingLots.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == pid, cancellationToken);
        }
        else
        {
            entity.CurrentParking = null;
        }

        var openTrip = await _db.VehicleTrips.AsNoTracking()
            .Where(x => x.VehicleId == id && OpenStatuses.Contains(x.Status))
            .OrderByDescending(x => x.PlannedStartAt)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(MapVehicle(entity, openTrip, DateTimeOffset.UtcNow));
    }

    [HttpDelete("vehicles/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageBusinessRecords)]
    public async Task<IActionResult> DeleteVehicle(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _db.Vehicles.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var hasOpen = await _db.VehicleTrips.AnyAsync(
            x => x.VehicleId == id && OpenStatuses.Contains(x.Status),
            cancellationToken);
        if (hasOpen)
        {
            return Conflict(new
            {
                error = "vehicle_busy",
                message = "Нельзя удалить машину с активной выдачей или бронью."
            });
        }

        // История поездок/заправок/ремонтов уходит вместе с машиной.
        await _db.VehicleTrips.Where(x => x.VehicleId == id).ExecuteDeleteAsync(cancellationToken);
        await _db.VehicleFuelRefuels.Where(x => x.VehicleId == id).ExecuteDeleteAsync(cancellationToken);
        await _db.VehicleRepairs.Where(x => x.VehicleId == id).ExecuteDeleteAsync(cancellationToken);
        _db.Vehicles.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("vehicles/{id:guid}/refuels")]
    public async Task<ActionResult<VehicleFuelRefuelResponse>> AddRefuel(
        Guid id,
        [FromBody] SaveVehicleFuelRefuelRequest request,
        CancellationToken cancellationToken)
    {
        var vehicle = await _db.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (vehicle is null)
        {
            return NotFound(new { error = "vehicle_not_found", message = "Машина не найдена." });
        }

        if (request.Liters <= 0)
        {
            return BadRequest(new { error = "liters_required", message = "Укажите объём заправки (литры)." });
        }

        if (request.PricePerLiter < 0)
        {
            return BadRequest(new { error = "price_negative", message = "Цена за литр не может быть отрицательной." });
        }

        var fuelType = (request.FuelType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fuelType))
        {
            return BadRequest(new { error = "fuel_type_required", message = "Укажите тип топлива." });
        }

        var liters = Math.Round(request.Liters, 2);
        var price = Math.Round(request.PricePerLiter, 2);
        var entity = new VehicleFuelRefuel
        {
            Id = Guid.NewGuid(),
            VehicleId = id,
            RefueledAt = request.RefueledAt ?? DateTimeOffset.UtcNow,
            Liters = liters,
            FuelType = fuelType.Length > 40 ? fuelType[..40] : fuelType,
            PricePerLiter = price,
            Cost = Math.Round(liters * price, 2),
            Note = NullIfEmpty(request.Note),
            CreatedByUserId = GetUserId(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.VehicleFuelRefuels.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new VehicleFuelRefuelResponse
        {
            Id = entity.Id,
            VehicleId = entity.VehicleId,
            RefueledAt = entity.RefueledAt,
            Liters = entity.Liters,
            FuelType = entity.FuelType,
            PricePerLiter = entity.PricePerLiter,
            Cost = entity.Cost,
            Note = entity.Note
        });
    }

    [HttpDelete("vehicles/{vehicleId:guid}/refuels/{refuelId:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageBusinessRecords)]
    public async Task<IActionResult> DeleteRefuel(
        Guid vehicleId,
        Guid refuelId,
        CancellationToken cancellationToken)
    {
        var entity = await _db.VehicleFuelRefuels
            .SingleOrDefaultAsync(x => x.Id == refuelId && x.VehicleId == vehicleId, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        _db.VehicleFuelRefuels.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("trips")]
    public async Task<ActionResult<VehicleTripResponse>> CreateTrip(
        [FromBody] CreateVehicleTripRequest request,
        CancellationToken cancellationToken)
    {
        var vehicle = await _db.Vehicles.SingleOrDefaultAsync(x => x.Id == request.VehicleId, cancellationToken);
        if (vehicle is null || !vehicle.IsActive)
        {
            return NotFound(new { error = "vehicle_not_found", message = "Машина не найдена." });
        }

        if (vehicle.IsInRepair)
        {
            return BadRequest(new { error = "vehicle_in_repair", message = "Машина в ремонте — выдача и бронь запрещены." });
        }

        var borrower = await _users.FindByIdAsync(request.BorrowerUserId.ToString());
        if (borrower is null || !borrower.IsActive)
        {
            return BadRequest(new { error = "borrower_not_found", message = "Подразделение (пользователь) не найдено." });
        }

        var start = request.PlannedStartAt ?? DateTimeOffset.UtcNow;
        var end = request.PlannedEndAt;
        if (end <= start)
        {
            return BadRequest(new { error = "invalid_range", message = "Конец должен быть позже начала." });
        }

        var conflict = await FindOverlapAsync(request.VehicleId, request.BorrowerUserId, start, end, excludeTripId: null, cancellationToken);
        if (conflict is not null)
        {
            return Conflict(new
            {
                error = "overlap",
                message = conflict
            });
        }

        var immediately = request.PlannedStartAt is null || request.PlannedStartAt <= DateTimeOffset.UtcNow.AddMinutes(1);
        var trip = new VehicleTrip
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicle.Id,
            BorrowerUserId = borrower.Id,
            BorrowerName = DisplayOf(borrower),
            Status = immediately ? VehicleTripStatus.Active : VehicleTripStatus.Reserved,
            PlannedStartAt = start,
            PlannedEndAt = end,
            ActualStartAt = immediately ? start : null,
            Purpose = NullIfEmpty(request.Purpose),
            OdometerStart = request.OdometerStart,
            FuelStart = request.FuelStart,
            Note = NullIfEmpty(request.Note),
            CreatedByUserId = GetUserId(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        if (immediately)
        {
            vehicle.CurrentParkingId = null;
        }

        _db.VehicleTrips.Add(trip);
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(MapTrip(trip, vehicle, null, DateTimeOffset.UtcNow));
    }

    [HttpPost("trips/{id:guid}/start")]
    public async Task<ActionResult<VehicleTripResponse>> StartTrip(
        Guid id,
        [FromBody] StartVehicleTripRequest? request,
        CancellationToken cancellationToken)
    {
        var trip = await _db.VehicleTrips.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        if (trip.Status != VehicleTripStatus.Reserved)
        {
            return BadRequest(new { error = "invalid_status", message = "Начать можно только бронь." });
        }

        var vehicle = await _db.Vehicles.SingleOrDefaultAsync(x => x.Id == trip.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        if (vehicle.IsInRepair)
        {
            return BadRequest(new { error = "vehicle_in_repair", message = "Машина в ремонте — старт запрещён." });
        }

        var now = DateTimeOffset.UtcNow;
        // Можно начать раньше PlannedStartAt — бронь «забираем в работу» досрочно.
        if (trip.PlannedEndAt <= now)
        {
            return BadRequest(new { error = "booking_expired", message = "Срок брони уже истёк — оформите новую выдачу." });
        }

        var conflict = await FindOverlapAsync(
            trip.VehicleId,
            trip.BorrowerUserId,
            now,
            trip.PlannedEndAt,
            excludeTripId: trip.Id,
            cancellationToken);
        if (conflict is not null)
        {
            return Conflict(new { error = "overlap", message = conflict });
        }

        trip.Status = VehicleTripStatus.Active;
        trip.ActualStartAt = now;
        if (now < trip.PlannedStartAt)
        {
            trip.PlannedStartAt = now;
        }

        if (request?.OdometerStart is int od)
        {
            trip.OdometerStart = od;
        }

        if (request?.FuelStart is decimal fuel)
        {
            trip.FuelStart = fuel;
        }

        vehicle.CurrentParkingId = null;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(MapTrip(trip, vehicle, null, now));
    }

    [HttpPost("trips/{id:guid}/return")]
    public async Task<ActionResult<VehicleTripResponse>> ReturnTrip(
        Guid id,
        [FromBody] ReturnVehicleTripRequest request,
        CancellationToken cancellationToken)
    {
        var trip = await _db.VehicleTrips.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        if (trip.Status is not (VehicleTripStatus.Active or VehicleTripStatus.Reserved))
        {
            return BadRequest(new { error = "invalid_status", message = "Вернуть можно активную выдачу или бронь." });
        }

        var vehicle = await _db.Vehicles.SingleOrDefaultAsync(x => x.Id == trip.VehicleId, cancellationToken);
        if (vehicle is null)
        {
            return NotFound();
        }

        ParkingLot? parking = null;
        if (request.ReturnParkingId is Guid parkingId)
        {
            parking = await _db.ParkingLots.SingleOrDefaultAsync(x => x.Id == parkingId && x.IsActive, cancellationToken);
            if (parking is null)
            {
                return BadRequest(new { error = "parking_not_found", message = "Стоянка не найдена." });
            }
        }

        var now = DateTimeOffset.UtcNow;
        var started = trip.ActualStartAt ?? trip.PlannedStartAt;
        var usedHours = Math.Round((decimal)(now - started).TotalHours, 2);
        if (usedHours < 0)
        {
            usedHours = 0;
        }

        var late = (int)Math.Ceiling((now - trip.PlannedEndAt).TotalMinutes);
        if (late < 0)
        {
            late = 0;
        }

        trip.Status = VehicleTripStatus.Returned;
        trip.ActualStartAt ??= started;
        trip.ActualEndAt = now;
        trip.UsedHours = usedHours;
        trip.LateMinutes = late;
        trip.OdometerEnd = request.OdometerEnd;
        trip.FuelEnd = request.FuelEnd;
        trip.ReturnParkingId = parking?.Id ?? vehicle.CurrentParkingId;
        if (!string.IsNullOrWhiteSpace(request.Note))
        {
            trip.Note = string.IsNullOrWhiteSpace(trip.Note)
                ? request.Note.Trim()
                : $"{trip.Note}\n{request.Note.Trim()}";
        }

        vehicle.CurrentParkingId = trip.ReturnParkingId;
        await _db.SaveChangesAsync(cancellationToken);

        parking ??= trip.ReturnParkingId is Guid rid
            ? await _db.ParkingLots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == rid, cancellationToken)
            : null;

        return Ok(MapTrip(trip, vehicle, parking, now));
    }

    [HttpPost("trips/{id:guid}/cancel")]
    public async Task<ActionResult<VehicleTripResponse>> CancelTrip(
        Guid id,
        CancellationToken cancellationToken)
    {
        var trip = await _db.VehicleTrips.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (trip is null)
        {
            return NotFound();
        }

        if (trip.Status != VehicleTripStatus.Reserved)
        {
            return BadRequest(new { error = "invalid_status", message = "Отменить можно только бронь." });
        }

        var vehicle = await _db.Vehicles.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == trip.VehicleId, cancellationToken);
        trip.Status = VehicleTripStatus.Cancelled;
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(MapTrip(trip, vehicle, null, DateTimeOffset.UtcNow));
    }

    private async Task<string?> FindOverlapAsync(
        Guid vehicleId,
        Guid borrowerUserId,
        DateTimeOffset start,
        DateTimeOffset end,
        Guid? excludeTripId,
        CancellationToken cancellationToken)
    {
        var open = await _db.VehicleTrips.AsNoTracking()
            .Where(x => OpenStatuses.Contains(x.Status)
                        && (excludeTripId == null || x.Id != excludeTripId)
                        && (x.VehicleId == vehicleId || x.BorrowerUserId == borrowerUserId))
            .ToListAsync(cancellationToken);

        foreach (var t in open)
        {
            var tStart = t.ActualStartAt ?? t.PlannedStartAt;
            // Активная выдача без возврата блокирует слот до факта сдачи.
            var tEnd = t.Status == VehicleTripStatus.Active && t.ActualEndAt is null
                ? DateTimeOffset.MaxValue
                : (t.ActualEndAt ?? t.PlannedEndAt);
            // Пересечение интервалов [start, end) ∩ [tStart, tEnd)
            if (start < tEnd && end > tStart)
            {
                if (t.VehicleId == vehicleId)
                {
                    return $"Машина уже занята/забронирована с {tStart:dd.MM HH:mm}.";
                }

                return $"У «{t.BorrowerName}» уже есть выезд/бронь с {tStart:dd.MM HH:mm}.";
            }
        }

        return null;
    }

    private async Task<List<TransportBorrowerOption>> LoadBorrowersAsync()
    {
        var users = await _users.GetUsersInRoleAsync(AppRoles.Initiator);
        return users
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Select(x => new TransportBorrowerOption
            {
                Id = x.Id,
                UserName = x.UserName ?? string.Empty,
                DisplayName = DisplayOf(x)
            })
            .ToList();
    }

    private static VehicleResponse MapVehicle(Vehicle v, VehicleTrip? trip, DateTimeOffset now)
    {
        var availability = "free";
        var overdue = false;
        if (trip is not null)
        {
            availability = trip.Status == VehicleTripStatus.Reserved ? "reserved" : "out";
            overdue = now > trip.PlannedEndAt;
        }

        return new VehicleResponse
        {
            Id = v.Id,
            PlateNumber = v.PlateNumber,
            Name = v.Name,
            VehicleType = v.VehicleType,
            Vin = v.Vin,
            IsInRepair = v.IsInRepair,
            CurrentParkingId = v.CurrentParkingId,
            CurrentParkingName = v.CurrentParking?.Name,
            IsActive = v.IsActive,
            Availability = availability,
            ActiveTripId = trip?.Id,
            ActiveBorrowerName = trip?.BorrowerName,
            ActivePlannedEndAt = trip?.PlannedEndAt,
            IsOverdue = overdue
        };
    }

    private void OpenRepair(Guid vehicleId)
    {
        _db.VehicleRepairs.Add(new VehicleRepair
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicleId,
            StartedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = GetUserId(),
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private async Task CloseOpenRepairAsync(Guid vehicleId, CancellationToken cancellationToken)
    {
        var open = await _db.VehicleRepairs
            .Where(x => x.VehicleId == vehicleId && x.EndedAt == null)
            .OrderByDescending(x => x.StartedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (open is not null)
        {
            open.EndedAt = DateTimeOffset.UtcNow;
        }
    }

    private static VehicleRepairResponse MapRepair(VehicleRepair r, DateTimeOffset now)
    {
        var end = r.EndedAt ?? now;
        var hours = (decimal)(end - r.StartedAt).TotalHours;
        return new VehicleRepairResponse
        {
            Id = r.Id,
            VehicleId = r.VehicleId,
            StartedAt = r.StartedAt,
            EndedAt = r.EndedAt,
            DurationHours = Math.Round(Math.Max(hours, 0), 2),
            IsOpen = r.EndedAt is null,
            Note = r.Note
        };
    }

    private static string? NormalizeVin(string? vin)
    {
        var value = NullIfEmpty(vin);
        return value is null ? null : value.ToUpperInvariant();
    }

    private static VehicleTripResponse MapTrip(
        VehicleTrip t,
        Vehicle? v,
        ParkingLot? parking,
        DateTimeOffset now) =>
        new()
        {
            Id = t.Id,
            VehicleId = t.VehicleId,
            PlateNumber = v?.PlateNumber ?? string.Empty,
            VehicleName = v?.Name ?? string.Empty,
            BorrowerUserId = t.BorrowerUserId,
            BorrowerName = t.BorrowerName,
            Status = t.Status,
            StatusLabel = StatusLabel(t.Status),
            PlannedStartAt = t.PlannedStartAt,
            PlannedEndAt = t.PlannedEndAt,
            ActualStartAt = t.ActualStartAt,
            ActualEndAt = t.ActualEndAt,
            UsedHours = t.UsedHours,
            LateMinutes = t.LateMinutes,
            Purpose = t.Purpose,
            OdometerStart = t.OdometerStart,
            OdometerEnd = t.OdometerEnd,
            FuelStart = t.FuelStart,
            FuelEnd = t.FuelEnd,
            ReturnParkingId = t.ReturnParkingId,
            ReturnParkingName = parking?.Name,
            Note = t.Note,
            IsOverdue = OpenStatuses.Contains(t.Status) && now > t.PlannedEndAt
        };

    private static string StatusLabel(VehicleTripStatus status) => status switch
    {
        VehicleTripStatus.Reserved => "Бронь",
        VehicleTripStatus.Active => "В работе",
        VehicleTripStatus.Returned => "Сдана",
        VehicleTripStatus.Cancelled => "Отменена",
        _ => status.ToString()
    };

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static string DisplayOf(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName)
            ? (user.UserName ?? string.Empty)
            : user.DisplayName;

    private static string NormalizePlate(string? plate) =>
        (plate ?? string.Empty).Trim().ToUpperInvariant();

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
