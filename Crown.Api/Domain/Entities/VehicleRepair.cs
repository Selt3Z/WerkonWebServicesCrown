namespace Crown.Domain.Entities;

/// <summary>Период ремонта машины (история галочки «В ремонте»).</summary>
public sealed class VehicleRepair
{
    public Guid Id { get; set; }

    public Guid VehicleId { get; set; }

    public Vehicle? Vehicle { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    /// <summary>null — ремонт ещё идёт.</summary>
    public DateTimeOffset? EndedAt { get; set; }

    public string? Note { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
