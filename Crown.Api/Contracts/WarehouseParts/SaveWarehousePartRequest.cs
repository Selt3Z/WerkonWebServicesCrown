namespace Crown.Contracts.WarehouseParts;

public sealed class SaveWarehousePartRequest
{
    public string Name { get; set; } = string.Empty;

    public string Code { get; set; } = string.Empty;

    public string? Barcode { get; set; }

    public string? Category { get; set; }

    public decimal DefaultUnitPrice { get; set; }

    public string DefaultUnit { get; set; } = "шт";

    public decimal QuantityOnHand { get; set; }

    public decimal MinQuantityOnHand { get; set; } = 1m;

    public string? Description { get; set; }
}
