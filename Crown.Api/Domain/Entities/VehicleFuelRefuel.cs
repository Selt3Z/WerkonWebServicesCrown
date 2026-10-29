namespace Crown.Domain.Entities;

/// <summary>Заправка машины: литры, тип топлива, стоимость.</summary>
public sealed class VehicleFuelRefuel
{
    public Guid Id { get; set; }

    public Guid VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    public DateTimeOffset RefueledAt { get; set; }

    public decimal Liters { get; set; }

    public string FuelType { get; set; } = string.Empty;

    /// <summary>Цена за литр; Cost = Liters * PricePerLiter.</summary>
    public decimal PricePerLiter { get; set; }

    public decimal Cost { get; set; }

    public string? Note { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
