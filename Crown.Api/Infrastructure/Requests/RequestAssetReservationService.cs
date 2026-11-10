using Microsoft.EntityFrameworkCore;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Requests;

/// <summary>Бронь/снятие брони и перемещение единиц по статусам заявки.</summary>
public sealed class RequestAssetReservationService(AppDbContext db)
{
    public async Task<(bool Ok, string? Error)> TryReserveAsync(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Kind == ServiceRequestKind.Equipment)
        {
            if (request.EquipmentAssetId is not Guid assetId)
            {
                return (false, "Выберите единицу оборудования.");
            }

            var asset = await db.EquipmentAssets
                .FirstOrDefaultAsync(x => x.Id == assetId, cancellationToken);
            if (asset is null)
            {
                return (false, "Единица оборудования не найдена.");
            }

            if (asset.Availability != EquipmentAvailability.Free
                && asset.ReservedByRequestId != request.Id)
            {
                return (false, "Единица оборудования уже занята или забронирована.");
            }

            asset.Availability = EquipmentAvailability.Reserved;
            asset.ReservedByRequestId = request.Id;
            return (true, null);
        }

        if (request.Kind == ServiceRequestKind.SpecTech)
        {
            // Accept без единицы разрешён — бронь только когда единица уже назначена.
            if (request.SpecialVehicleUnitId is not Guid unitId)
            {
                return (true, null);
            }

            var unit = await db.SpecialVehicleUnits
                .FirstOrDefaultAsync(x => x.Id == unitId && x.IsActive, cancellationToken);
            if (unit is null)
            {
                return (false, "Единица спецтехники не найдена.");
            }

            if (unit.Availability != SpecialVehicleAvailability.Free
                && unit.ReservedByRequestId != request.Id)
            {
                return (false, "Единица спецтехники уже занята или забронирована.");
            }

            unit.Availability = SpecialVehicleAvailability.Reserved;
            unit.ReservedByRequestId = request.Id;
            return (true, null);
        }

        return (true, null);
    }

    public async Task ReleaseAsync(ServiceRequest request, CancellationToken cancellationToken)
    {
        if (request.EquipmentAssetId is Guid assetId)
        {
            var asset = await db.EquipmentAssets
                .FirstOrDefaultAsync(x => x.Id == assetId, cancellationToken);
            if (asset is not null && asset.ReservedByRequestId == request.Id)
            {
                asset.Availability = EquipmentAvailability.Free;
                asset.ReservedByRequestId = null;
            }
        }

        if (request.SpecialVehicleUnitId is Guid unitId)
        {
            var unit = await db.SpecialVehicleUnits
                .FirstOrDefaultAsync(x => x.Id == unitId, cancellationToken);
            if (unit is not null && unit.ReservedByRequestId == request.Id)
            {
                unit.Availability = SpecialVehicleAvailability.Free;
                unit.ReservedByRequestId = null;
            }
        }
    }

    public async Task<(bool Ok, string? Error)> CompleteEquipmentMoveAsync(
        ServiceRequest request,
        Guid? byUserId,
        string? byDisplayName,
        CancellationToken cancellationToken)
    {
        if (request.Kind != ServiceRequestKind.Equipment || request.EquipmentAssetId is not Guid assetId)
        {
            return (true, null);
        }

        var asset = await db.EquipmentAssets
            .FirstOrDefaultAsync(x => x.Id == assetId, cancellationToken);
        if (asset is null)
        {
            return (false, "Единица оборудования не найдена.");
        }

        var toUserId = request.InitiatorUserId;
        var toLabel = request.InitiatorName;
        db.EquipmentMovements.Add(new EquipmentMovement
        {
            Id = Guid.NewGuid(),
            EquipmentAssetId = asset.Id,
            OccurredAt = DateTimeOffset.UtcNow,
            FromLocationUserId = asset.CurrentLocationUserId,
            FromLocationLabel = asset.CurrentLocationLabel,
            ToLocationUserId = toUserId,
            ToLocationLabel = toLabel,
            ServiceRequestId = request.Id,
            Note = $"По заявке №{request.Id}",
            ByUserId = byUserId,
            ByUserDisplayName = byDisplayName
        });

        asset.CurrentLocationUserId = toUserId;
        asset.CurrentLocationLabel = toLabel;
        asset.Availability = EquipmentAvailability.Free;
        asset.ReservedByRequestId = null;
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> MarkSpecArrivedAsync(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SpecialVehicleUnitId is not Guid unitId)
        {
            return (false, "Единица спецтехники не указана.");
        }

        var unit = await db.SpecialVehicleUnits
            .FirstOrDefaultAsync(x => x.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return (false, "Единица спецтехники не найдена.");
        }

        if (unit.ReservedByRequestId != request.Id
            && unit.Availability != SpecialVehicleAvailability.Reserved
            && unit.Availability != SpecialVehicleAvailability.InUse)
        {
            return (false, "Спецтехника не забронирована этой заявкой.");
        }

        unit.Availability = SpecialVehicleAvailability.InUse;
        unit.ReservedByRequestId = request.Id;
        request.SpecArrivedAt = DateTimeOffset.UtcNow;
        return (true, null);
    }

    public async Task<(bool Ok, string? Error)> MarkSpecReleasedAsync(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        // Завершение без назначенной единицы — ок (приняли в работу заранее).
        if (request.SpecialVehicleUnitId is not Guid unitId)
        {
            request.SpecReleasedAt ??= DateTimeOffset.UtcNow;
            return (true, null);
        }

        var unit = await db.SpecialVehicleUnits
            .FirstOrDefaultAsync(x => x.Id == unitId, cancellationToken);
        if (unit is null)
        {
            return (false, "Единица спецтехники не найдена.");
        }

        unit.Availability = SpecialVehicleAvailability.Free;
        unit.ReservedByRequestId = null;
        request.SpecReleasedAt ??= DateTimeOffset.UtcNow;
        return (true, null);
    }
}
