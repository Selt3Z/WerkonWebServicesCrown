namespace Crown.Domain.Entities;

/// <summary>Единица спецтехники (ёмкость по типу; без истории поездок).</summary>
public sealed class SpecialVehicleUnit
{
    public Guid Id { get; set; }

    /// <summary>Тип/название: кран, ППУ, бочка…</summary>
    public string Name { get; set; } = string.Empty;

    public string? PlateOrNote { get; set; }

    public SpecialVehicleAvailability Availability { get; set; } = SpecialVehicleAvailability.Free;

    public long? ReservedByRequestId { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
