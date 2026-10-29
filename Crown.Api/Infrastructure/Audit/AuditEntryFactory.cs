using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Audit;

public sealed class AuditEntryFactory
{
public Task<List<AuditLogEntry>> CreateEntriesAsync(
        AppDbContext dbContext,
        IEnumerable<EntityEntry> entries,
        Guid? userId,
        string userDisplayName,
        CancellationToken cancellationToken)
    {
        var auditEntries = new List<AuditLogEntry>();
        var occurredAtUtc = DateTime.UtcNow;

        foreach (var entry in entries)
        {
            if (entry.Entity is AuditLogEntry)
            {
                continue;
            }

            var action = entry.State switch
            {
                EntityState.Added => AuditActions.Created,
                EntityState.Modified => GetModifiedAction(entry),
                EntityState.Deleted => AuditActions.Deleted,
                _ => null
            };

            if (action is null)
            {
                continue;
            }

            if (!TryMapEntry(entry, action, out var entityType, out var entityId, out var summary, out var entityUrl))
            {
                continue;
            }

            auditEntries.Add(new AuditLogEntry
            {
                Id = Guid.NewGuid(),
                OccurredAtUtc = occurredAtUtc,
                UserId = userId,
                UserDisplayName = userDisplayName,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                Summary = summary,
                EntityUrl = entityUrl
            });
        }

        return Task.FromResult(auditEntries);
    }
    private static string GetModifiedAction(EntityEntry entry)
    {
        var isArchivedProperty = entry.Metadata.FindProperty("IsArchived") is null
            ? null
            : entry.Property("IsArchived");

        if (isArchivedProperty is { IsModified: true } && isArchivedProperty.CurrentValue is bool isArchived)
        {
            return isArchived ? AuditActions.Archived : AuditActions.Restored;
        }

        return AuditActions.Updated;
    }

    private bool TryMapEntry(
        EntityEntry entry,
        string action,
        out string entityType,
        out Guid entityId,
        out string summary,
        out string? entityUrl)
    {
        entityType = string.Empty;
        entityId = Guid.Empty;
        summary = string.Empty;
        entityUrl = null;

        switch (entry.Entity)
        {
            case CatalogPart catalogPart:
                entityType = AuditEntityTypes.CatalogPart;
                entityId = GetEntityId(entry, catalogPart.Id);
                summary = GetStringValue(entry, nameof(CatalogPart.Name), catalogPart.Name);
                entityUrl = action == AuditActions.Deleted ? null : $"/catalog-parts/edit/{entityId}";
                return true;

            case WarehousePart warehousePart:
                entityType = AuditEntityTypes.WarehousePart;
                entityId = GetEntityId(entry, warehousePart.Id);
                summary = GetStringValue(entry, nameof(WarehousePart.Name), warehousePart.Name);
                entityUrl = action == AuditActions.Deleted ? null : $"/warehouse/edit/{entityId}";
                return true;

            case ServiceRequest serviceRequest:
                entityType = AuditEntityTypes.ServiceRequest;
                entityId = ToAuditEntityId(GetLongEntityId(entry, serviceRequest.Id));
                summary = $"Заявка №{GetLongEntityId(entry, serviceRequest.Id)} · {serviceRequest.InitiatorName}";
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : $"/requests/{GetLongEntityId(entry, serviceRequest.Id)}";
                return true;

            case RequestAttachment attachment:
                entityType = AuditEntityTypes.ServiceRequest;
                entityId = ToAuditEntityId(attachment.ServiceRequestId);
                summary = $"Вложение: {GetStringValue(entry, nameof(RequestAttachment.FileName), attachment.FileName)}";
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : $"/requests/{attachment.ServiceRequestId}";
                return true;

            case Vehicle vehicle:
                entityType = AuditEntityTypes.Vehicle;
                entityId = GetEntityId(entry, vehicle.Id);
                summary = $"{GetStringValue(entry, nameof(Vehicle.PlateNumber), vehicle.PlateNumber)} · {vehicle.Name}";
                entityUrl = action == AuditActions.Deleted ? null : $"/transport/vehicles/{vehicle.Id}";
                return true;

            case VehicleFuelRefuel refuel:
                entityType = AuditEntityTypes.VehicleFuelRefuel;
                entityId = GetEntityId(entry, refuel.Id);
                summary = $"{refuel.Liters:0.##} л · {refuel.FuelType} · {refuel.Cost:0.##}";
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : $"/transport/vehicles/{refuel.VehicleId}";
                return true;

            case VehicleRepair repair:
                entityType = AuditEntityTypes.VehicleRepair;
                entityId = GetEntityId(entry, repair.Id);
                summary = repair.EndedAt is null
                    ? $"Ремонт с {repair.StartedAt:dd.MM.yyyy HH:mm}"
                    : $"Ремонт {repair.StartedAt:dd.MM.yyyy} — {repair.EndedAt:dd.MM.yyyy HH:mm}";
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : $"/transport/vehicles/{repair.VehicleId}";
                return true;

            case ParkingLot parking:
                entityType = AuditEntityTypes.ParkingLot;
                entityId = GetEntityId(entry, parking.Id);
                summary = GetStringValue(entry, nameof(ParkingLot.Name), parking.Name);
                entityUrl = action == AuditActions.Deleted ? null : "/transport";
                return true;

            case VehicleTrip trip:
                entityType = AuditEntityTypes.VehicleTrip;
                entityId = GetEntityId(entry, trip.Id);
                summary = $"{trip.BorrowerName} · {trip.Status}";
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : $"/transport/vehicles/{trip.VehicleId}";
                return true;

            default:
                return false;
        }
    }

    private static Guid GetEntityId(EntityEntry entry, Guid currentId) =>
        entry.State == EntityState.Deleted
            ? entry.OriginalValues.GetValue<Guid>("Id")
            : currentId;

    private static long GetLongEntityId(EntityEntry entry, long currentId) =>
        entry.State == EntityState.Deleted
            ? entry.OriginalValues.GetValue<long>("Id")
            : currentId;

    /// <summary>В AuditLog EntityId — Guid; для long-ключей кладём стабильный Guid из числа.</summary>
    private static Guid ToAuditEntityId(long id)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes, id);
        return new Guid(bytes);
    }

    private static string GetStringValue(EntityEntry entry, string propertyName, string currentValue) =>
        entry.State == EntityState.Deleted
            ? entry.OriginalValues.GetValue<string>(propertyName) ?? string.Empty
            : currentValue;
}
