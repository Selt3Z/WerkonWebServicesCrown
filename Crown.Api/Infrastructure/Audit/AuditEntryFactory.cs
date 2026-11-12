using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Audit;

public sealed class AuditEntryFactory
{
    /// <summary>Шум Identity/сессии — не пишем в журнал.</summary>
    private static readonly HashSet<string> UserNoiseProperties = new(StringComparer.Ordinal)
    {
        nameof(ApplicationUser.LastActivityAtUtc),
        nameof(ApplicationUser.ConcurrencyStamp),
        nameof(ApplicationUser.SecurityStamp),
        nameof(ApplicationUser.LockoutEnd),
        nameof(ApplicationUser.AccessFailedCount),
        "NormalizedUserName",
        "NormalizedEmail"
    };

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

            // Юзер: только осмысленные правки (не LastActivity и т.п.).
            if (entry.Entity is ApplicationUser && entry.State == EntityState.Modified
                && !HasMeaningfulUserChange(entry))
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

    private static bool HasMeaningfulUserChange(EntityEntry entry) =>
        entry.Properties.Any(p => p.IsModified && !UserNoiseProperties.Contains(p.Metadata.Name));

    private static string GetModifiedAction(EntityEntry entry)
    {
        var isArchivedProperty = entry.Metadata.FindProperty("IsArchived") is null
            ? null
            : entry.Property("IsArchived");

        if (isArchivedProperty is { IsModified: true } && isArchivedProperty.CurrentValue is bool isArchived)
        {
            return isArchived ? AuditActions.Archived : AuditActions.Restored;
        }

        // Документация: soft-delete через DeletedAt.
        var deletedAtProperty = entry.Metadata.FindProperty("DeletedAt") is null
            ? null
            : entry.Property("DeletedAt");
        if (deletedAtProperty is { IsModified: true })
        {
            var wasDeleted = deletedAtProperty.OriginalValue is not null;
            var isDeleted = deletedAtProperty.CurrentValue is not null;
            if (!wasDeleted && isDeleted)
            {
                return AuditActions.Archived;
            }

            if (wasDeleted && !isDeleted)
            {
                return AuditActions.Restored;
            }
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
                entityUrl = action is AuditActions.Deleted or AuditActions.Archived
                    ? null
                    : $"/catalog-parts/edit/{entityId}";
                return true;

            case WarehousePart warehousePart:
                entityType = AuditEntityTypes.WarehousePart;
                entityId = GetEntityId(entry, warehousePart.Id);
                summary = GetStringValue(entry, nameof(WarehousePart.Name), warehousePart.Name);
                entityUrl = action is AuditActions.Deleted or AuditActions.Archived
                    ? null
                    : $"/warehouse/edit/{entityId}";
                return true;

            case WarehouseStockMovement movement:
                // Движения склада — в том же типе, что и позиция.
                entityType = AuditEntityTypes.WarehousePart;
                entityId = movement.WarehousePartId;
                summary = $"Движение {movement.QuantityDelta:+0.##;-0.##;0} → {movement.QuantityAfter:0.##}"
                    + (string.IsNullOrWhiteSpace(movement.Reason) ? string.Empty : $" · {movement.Reason}");
                entityUrl = $"/warehouse/edit/{movement.WarehousePartId}";
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
                entityUrl = action is AuditActions.Deleted or AuditActions.Archived
                    ? null
                    : $"/transport/vehicles/{vehicle.Id}";
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

            case EquipmentAsset asset:
                entityType = AuditEntityTypes.EquipmentAsset;
                entityId = GetEntityId(entry, asset.Id);
                var inv = GetStringValue(entry, nameof(EquipmentAsset.InventoryNumber), asset.InventoryNumber);
                var name = GetStringValue(entry, nameof(EquipmentAsset.Name), asset.Name);
                summary = string.IsNullOrWhiteSpace(inv) ? name : $"{inv} · {name}";
                entityUrl = action is AuditActions.Deleted or AuditActions.Archived
                    ? null
                    : $"/equipment/{entityId}";
                return true;

            case EquipmentMovement eqMove:
                entityType = AuditEntityTypes.EquipmentAsset;
                entityId = eqMove.EquipmentAssetId;
                summary = $"Перемещение → {eqMove.ToLocationLabel ?? "—"}"
                    + (string.IsNullOrWhiteSpace(eqMove.Note) ? string.Empty : $" · {eqMove.Note}");
                entityUrl = $"/equipment/{eqMove.EquipmentAssetId}";
                return true;

            case SpecialVehicleUnit unit:
                entityType = AuditEntityTypes.SpecialVehicleUnit;
                entityId = GetEntityId(entry, unit.Id);
                var plate = entry.State == EntityState.Deleted
                    ? entry.OriginalValues.GetValue<string?>(nameof(SpecialVehicleUnit.PlateOrNote))
                    : unit.PlateOrNote;
                var unitName = GetStringValue(entry, nameof(SpecialVehicleUnit.Name), unit.Name);
                summary = string.IsNullOrWhiteSpace(plate) ? unitName : $"{unitName} · {plate}";
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : $"/special-vehicles/edit/{entityId}";
                return true;

            case DocumentationFolder folder:
                entityType = AuditEntityTypes.Documentation;
                entityId = GetEntityId(entry, folder.Id);
                summary = $"Папка: {GetStringValue(entry, nameof(DocumentationFolder.Name), folder.Name)}";
                entityUrl = action is AuditActions.Deleted or AuditActions.Archived
                    ? null
                    : "/documentation";
                return true;

            case DocumentationFile file:
                entityType = AuditEntityTypes.Documentation;
                entityId = GetEntityId(entry, file.Id);
                summary = $"Файл: {GetStringValue(entry, nameof(DocumentationFile.FileName), file.FileName)}";
                entityUrl = action is AuditActions.Deleted or AuditActions.Archived
                    ? null
                    : "/documentation";
                return true;

            case BrigadeStoppage stoppage:
                entityType = AuditEntityTypes.BrigadeStoppage;
                entityId = GetEntityId(entry, stoppage.Id);
                var brigadeName = entry.State == EntityState.Deleted
                    ? entry.OriginalValues.GetValue<string>(nameof(BrigadeStoppage.BrigadeName)) ?? string.Empty
                    : stoppage.BrigadeName;
                var reportDate = entry.State == EntityState.Deleted
                    ? entry.OriginalValues.GetValue<DateOnly>(nameof(BrigadeStoppage.ReportDate))
                    : stoppage.ReportDate;
                var stoppedAt = entry.State == EntityState.Deleted
                    ? entry.OriginalValues.GetValue<TimeOnly>(nameof(BrigadeStoppage.StoppedAt))
                    : stoppage.StoppedAt;
                summary = $"{brigadeName} · {reportDate:dd.MM.yyyy} · {stoppedAt:HH:mm}";
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : $"/brigade-rating/stoppage/{entityId}";
                return true;

            case StoppageActItem actItem:
                entityType = AuditEntityTypes.StoppageActItem;
                entityId = GetEntityId(entry, actItem.Id);
                summary = GetStringValue(entry, nameof(StoppageActItem.Text), actItem.Text);
                entityUrl = action == AuditActions.Deleted
                    ? null
                    : "/brigade-rating/act-items";
                return true;

            case AppSetting setting:
                entityType = AuditEntityTypes.AppSetting;
                var key = entry.State == EntityState.Deleted
                    ? entry.OriginalValues.GetValue<string>(nameof(AppSetting.Key)) ?? string.Empty
                    : setting.Key;
                entityId = GuidFromKey(key);
                var value = entry.State == EntityState.Deleted
                    ? entry.OriginalValues.GetValue<string>(nameof(AppSetting.Value)) ?? string.Empty
                    : setting.Value;
                summary = $"{key} = {Truncate(value, 80)}";
                entityUrl = "/settings";
                return true;

            case ApplicationUser appUser:
                entityType = AuditEntityTypes.User;
                entityId = GetEntityId(entry, appUser.Id);
                var display = entry.State == EntityState.Deleted
                    ? (entry.OriginalValues.GetValue<string>(nameof(ApplicationUser.DisplayName))
                       ?? entry.OriginalValues.GetValue<string>(nameof(ApplicationUser.UserName))
                       ?? string.Empty)
                    : (string.IsNullOrWhiteSpace(appUser.DisplayName) ? appUser.UserName ?? string.Empty : appUser.DisplayName);
                summary = display;
                entityUrl = action == AuditActions.Deleted ? null : "/users";
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

    private static Guid GuidFromKey(string key)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(key ?? string.Empty));
        return new Guid(hash);
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";

    private static string GetStringValue(EntityEntry entry, string propertyName, string currentValue) =>
        entry.State == EntityState.Deleted
            ? entry.OriginalValues.GetValue<string>(propertyName) ?? string.Empty
            : currentValue;
}
