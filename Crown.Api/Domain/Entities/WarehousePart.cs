namespace Crown.Domain.Entities;

public sealed class WarehousePart
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Barcode { get; set; }

    public string? Category { get; set; }

    public decimal DefaultUnitPrice { get; set; }

    public string DefaultUnit { get; set; } = "шт";

    public decimal QuantityOnHand { get; set; }

    /// <summary>Остаток меньше или равен этому значению считается дефицитом.</summary>
    public decimal MinQuantityOnHand { get; set; } = 1m;

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public DateTime? ArchivedAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<WarehouseStockMovement> StockMovements { get; set; } = new List<WarehouseStockMovement>();
}
