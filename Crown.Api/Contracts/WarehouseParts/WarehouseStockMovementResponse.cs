using Crown.Domain.Entities;

namespace Crown.Contracts.WarehouseParts;

public sealed class WarehouseStockMovementResponse
{
    public Guid Id { get; set; }

    public Guid WarehousePartId { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public decimal QuantityDelta { get; set; }

    public decimal QuantityAfter { get; set; }

    public WarehouseStockMovementKind Kind { get; set; }

    public string? Reason { get; set; }

    public Guid? ServiceRequestId { get; set; }

    public Guid? UserId { get; set; }

    public string? UserDisplayName { get; set; }
}
