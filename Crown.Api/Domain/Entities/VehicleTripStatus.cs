namespace Crown.Domain.Entities;

public enum VehicleTripStatus
{
    /// <summary>Бронь на будущее.</summary>
    Reserved = 1,

    /// <summary>Машина сейчас у подразделения.</summary>
    Active = 2,

    /// <summary>Сдана.</summary>
    Returned = 3,

    /// <summary>Бронь снята.</summary>
    Cancelled = 4
}
