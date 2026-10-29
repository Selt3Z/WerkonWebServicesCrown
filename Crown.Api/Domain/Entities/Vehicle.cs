namespace Crown.Domain.Entities;

/// <summary>Машина парка. CurrentParkingId — где стоит, пока свободна.</summary>
public sealed class Vehicle
{
    public Guid Id { get; set; }

    public string PlateNumber { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? VehicleType { get; set; }

    /// <summary>VIN — необязательно.</summary>
    public string? Vin { get; set; }

    /// <summary>В ремонте — выдавать/бронировать нельзя.</summary>
    public bool IsInRepair { get; set; }

    public Guid? CurrentParkingId { get; set; }

    public ParkingLot? CurrentParking { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
