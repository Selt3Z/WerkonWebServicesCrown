namespace Crown.Domain.Entities;

/// <summary>Тип складского движения остатка.</summary>
public enum WarehouseStockMovementKind
{
    Receipt = 1,
    Adjustment = 2,
    Deduction = 3,
    Return = 4
}
