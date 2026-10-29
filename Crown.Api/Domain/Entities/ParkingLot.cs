namespace Crown.Domain.Entities;

/// <summary>Стоянка / база, где стоит свободная машина.</summary>
public sealed class ParkingLot
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
