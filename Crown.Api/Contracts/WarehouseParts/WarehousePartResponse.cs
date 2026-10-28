namespace Crown.Contracts.WarehouseParts;

public sealed class WarehousePartResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Barcode { get; set; }

    public string? Category { get; set; }

    public decimal DefaultUnitPrice { get; set; }

    public string DefaultUnit { get; set; } = string.Empty;

    public decimal QuantityOnHand { get; set; }

    public decimal MinQuantityOnHand { get; set; }

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
