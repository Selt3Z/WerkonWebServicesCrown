namespace Crown.Domain.Entities;

public sealed class WarehouseStockMovement
{
    public Guid Id { get; set; }

    public Guid WarehousePartId { get; set; }
    public WarehousePart WarehousePart { get; set; } = null!;

    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Знаковое изменение остатка (списание — отрицательное).</summary>
    public decimal QuantityDelta { get; set; }

    public decimal QuantityAfter { get; set; }

    public WarehouseStockMovementKind Kind { get; set; }

    public string? Reason { get; set; }

    public Guid? ServiceRequestId { get; set; }

    public Guid? UserId { get; set; }

    public string? UserDisplayName { get; set; }
}
