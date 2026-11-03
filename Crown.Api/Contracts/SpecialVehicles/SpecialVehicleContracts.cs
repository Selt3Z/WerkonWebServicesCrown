using Crown.Domain.Entities;

namespace Crown.Contracts.SpecialVehicles;

public sealed class SpecialVehicleUnitResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PlateOrNote { get; set; }
    public SpecialVehicleAvailability Availability { get; set; }
    public long? ReservedByRequestId { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class SaveSpecialVehicleUnitRequest
{
    public string Name { get; set; } = string.Empty;
    public string? PlateOrNote { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class SpecialVehicleTypeAvailabilityResponse
{
    public string Name { get; set; } = string.Empty;
    public int FreeCount { get; set; }
    public int TotalActive { get; set; }
}
