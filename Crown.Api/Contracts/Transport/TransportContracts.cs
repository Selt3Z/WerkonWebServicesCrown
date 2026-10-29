using Crown.Domain.Entities;

namespace Crown.Contracts.Transport;

public sealed class ParkingLotResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; }
}

public sealed class SaveParkingLotRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Address { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class VehicleResponse
{
    public Guid Id { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public string? Vin { get; set; }
    public bool IsInRepair { get; set; }
    public Guid? CurrentParkingId { get; set; }
    public string? CurrentParkingName { get; set; }
    public bool IsActive { get; set; }

    /// <summary>free | reserved | out</summary>
    public string Availability { get; set; } = "free";

    public Guid? ActiveTripId { get; set; }
    public string? ActiveBorrowerName { get; set; }
    public DateTimeOffset? ActivePlannedEndAt { get; set; }
    public bool IsOverdue { get; set; }
}

public sealed class SaveVehicleRequest
{
    public string PlateNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? VehicleType { get; set; }
    public string? Vin { get; set; }
    public bool IsInRepair { get; set; }
    public Guid? CurrentParkingId { get; set; }
    public bool IsActive { get; set; } = true;
}

public sealed class VehicleFuelRefuelResponse
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset RefueledAt { get; set; }
    public decimal Liters { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal PricePerLiter { get; set; }
    public decimal Cost { get; set; }
    public string? Note { get; set; }
}

public sealed class SaveVehicleFuelRefuelRequest
{
    public DateTimeOffset? RefueledAt { get; set; }
    public decimal Liters { get; set; }
    public string FuelType { get; set; } = string.Empty;
    public decimal PricePerLiter { get; set; }
    public string? Note { get; set; }
}

public sealed class VehicleRepairResponse
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public decimal? DurationHours { get; set; }
    public bool IsOpen { get; set; }
    public string? Note { get; set; }
}

public sealed class VehicleDetailResponse
{
    public VehicleResponse Vehicle { get; set; } = new();
    public List<VehicleFuelRefuelResponse> Refuels { get; set; } = [];
    public List<VehicleTripResponse> Trips { get; set; } = [];
    public List<VehicleRepairResponse> Repairs { get; set; } = [];
    public decimal TotalLiters { get; set; }
    public decimal TotalCost { get; set; }
}

public sealed class VehicleTripResponse
{
    public Guid Id { get; set; }
    public Guid VehicleId { get; set; }
    public string PlateNumber { get; set; } = string.Empty;
    public string VehicleName { get; set; } = string.Empty;
    public Guid BorrowerUserId { get; set; }
    public string BorrowerName { get; set; } = string.Empty;
    public VehicleTripStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public DateTimeOffset PlannedStartAt { get; set; }
    public DateTimeOffset PlannedEndAt { get; set; }
    public DateTimeOffset? ActualStartAt { get; set; }
    public DateTimeOffset? ActualEndAt { get; set; }
    public decimal? UsedHours { get; set; }
    public int? LateMinutes { get; set; }
    public string? Purpose { get; set; }
    public int? OdometerStart { get; set; }
    public int? OdometerEnd { get; set; }
    public decimal? FuelStart { get; set; }
    public decimal? FuelEnd { get; set; }
    public Guid? ReturnParkingId { get; set; }
    public string? ReturnParkingName { get; set; }
    public string? Note { get; set; }
    public bool IsOverdue { get; set; }
}

public sealed class TransportBoardResponse
{
    public List<VehicleResponse> Free { get; set; } = [];
    public List<VehicleResponse> Busy { get; set; } = [];
    public List<ParkingLotResponse> Parkings { get; set; } = [];
    public List<VehicleTripResponse> RecentReturns { get; set; } = [];
    public List<TransportBorrowerOption> Borrowers { get; set; } = [];
}

public sealed class TransportBorrowerOption
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class CreateVehicleTripRequest
{
    public Guid VehicleId { get; set; }
    public Guid BorrowerUserId { get; set; }

    /// <summary>null → сейчас (выдача). Иначе бронь/старт.</summary>
    public DateTimeOffset? PlannedStartAt { get; set; }

    public DateTimeOffset PlannedEndAt { get; set; }

    public string? Purpose { get; set; }
    public int? OdometerStart { get; set; }
    public decimal? FuelStart { get; set; }
    public string? Note { get; set; }
}

public sealed class StartVehicleTripRequest
{
    public int? OdometerStart { get; set; }
    public decimal? FuelStart { get; set; }
}

public sealed class ReturnVehicleTripRequest
{
    public Guid? ReturnParkingId { get; set; }
    public int? OdometerEnd { get; set; }
    public decimal? FuelEnd { get; set; }
    public string? Note { get; set; }
}
