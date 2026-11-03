namespace Crown.Domain.Entities;

/// <summary>Единица оборудования с текущим местом и бронью под заявку.</summary>
public sealed class EquipmentAsset
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string InventoryNumber { get; set; } = string.Empty;

    public string? Category { get; set; }

    public string? Description { get; set; }

    public Guid? CurrentLocationUserId { get; set; }

    public string? CurrentLocationLabel { get; set; }

    /// <summary>Подразделение-владелец (куда «принадлежит», обычно где создано).</summary>
    public Guid? OwnerUserId { get; set; }

    public string? OwnerLabel { get; set; }

    public EquipmentAvailability Availability { get; set; } = EquipmentAvailability.Free;

    public long? ReservedByRequestId { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public List<EquipmentMovement> Movements { get; set; } = [];
}
