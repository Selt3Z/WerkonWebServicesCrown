using Crown.Domain.Entities;

namespace Crown.Contracts.Equipment;

public class EquipmentAssetResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string InventoryNumber { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public Guid? CurrentLocationUserId { get; set; }
    public string? CurrentLocationLabel { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? OwnerLabel { get; set; }
    public EquipmentAvailability Availability { get; set; }
    public long? ReservedByRequestId { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class EquipmentMovementResponse
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public Guid? FromLocationUserId { get; set; }
    public string? FromLocationLabel { get; set; }
    public Guid? ToLocationUserId { get; set; }
    public string? ToLocationLabel { get; set; }
    public long? ServiceRequestId { get; set; }
    public string? Note { get; set; }
    public string? ByUserDisplayName { get; set; }
}

public sealed class EquipmentAssetDetailResponse : EquipmentAssetResponse
{
    public List<EquipmentMovementResponse> Movements { get; set; } = [];
}

public sealed class SaveEquipmentAssetRequest
{
    public string Name { get; set; } = string.Empty;
    public string InventoryNumber { get; set; } = string.Empty;
    public string? Category { get; set; }
    public string? Description { get; set; }
    public Guid? CurrentLocationUserId { get; set; }
    public string? CurrentLocationLabel { get; set; }
    public Guid? OwnerUserId { get; set; }
    public string? OwnerLabel { get; set; }
}

public sealed class EquipmentLocationOptionResponse
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
