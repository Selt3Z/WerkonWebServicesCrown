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
                entityUrl = action == AuditActions.Deleted ? null : $"/catalog-parts/{entityId}/edit";
                return true;

            case WarehousePart warehousePart:
                entityType = AuditEntityTypes.WarehousePart;
                entityId = GetEntityId(entry, warehousePart.Id);
                summary = GetStringValue(entry, nameof(WarehousePart.Name), warehousePart.Name);
                entityUrl = action == AuditActions.Deleted ? null : $"/warehouse-parts/{entityId}/edit";
                return true;

            default:
                return false;
        }
    }

    private static Guid GetEntityId(EntityEntry entry, Guid currentId) =>
        entry.State == EntityState.Deleted
            ? entry.OriginalValues.GetValue<Guid>("Id")
            : currentId;

    private static string GetStringValue(EntityEntry entry, string propertyName, string currentValue) =>
        entry.State == EntityState.Deleted
            ? entry.OriginalValues.GetValue<string>(propertyName) ?? string.Empty
            : currentValue;
}
