namespace Crown.Domain.Entities;

/// <summary>Запись «путешествия» единицы оборудования.</summary>
public sealed class EquipmentMovement
{
    public Guid Id { get; set; }

    public Guid EquipmentAssetId { get; set; }

    public EquipmentAsset? EquipmentAsset { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public Guid? FromLocationUserId { get; set; }

    public string? FromLocationLabel { get; set; }

    public Guid? ToLocationUserId { get; set; }

    public string? ToLocationLabel { get; set; }

    public long? ServiceRequestId { get; set; }

    public string? Note { get; set; }

    public Guid? ByUserId { get; set; }

    public string? ByUserDisplayName { get; set; }
}
