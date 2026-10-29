namespace Crown.Domain.Entities;

/// <summary>
/// Выдача / бронь машины. BorrowerUserId — пользователь-«подразделение».
/// По часам: не пересекаем активные Reserved/Active на одну машину и на одного заёмщика.
/// </summary>
public sealed class VehicleTrip
{
    public Guid Id { get; set; }

    public Guid VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    /// <summary>Кто взял (учётная запись подразделения).</summary>
    public Guid BorrowerUserId { get; set; }

    public string BorrowerName { get; set; } = string.Empty;

    public VehicleTripStatus Status { get; set; }

    public DateTimeOffset PlannedStartAt { get; set; }

    public DateTimeOffset PlannedEndAt { get; set; }

    public DateTimeOffset? ActualStartAt { get; set; }

    public DateTimeOffset? ActualEndAt { get; set; }

    /// <summary>Часы факт. пользования (при возврате).</summary>
    public decimal? UsedHours { get; set; }

    /// <summary>Опоздание в минутах относительно PlannedEndAt (при возврате).</summary>
    public int? LateMinutes { get; set; }

    public string? Purpose { get; set; }

    public int? OdometerStart { get; set; }

    public int? OdometerEnd { get; set; }

    public decimal? FuelStart { get; set; }

    public decimal? FuelEnd { get; set; }

    public Guid? ReturnParkingId { get; set; }

    public string? Note { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
