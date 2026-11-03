using Microsoft.EntityFrameworkCore;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;

namespace Crown.Tests;

internal static class TestDb
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"crown-tests-{Guid.NewGuid():N}")
            .Options;
        return new AppDbContext(options);
    }

    public static ServiceRequest Request(
        ServiceRequestKind kind = ServiceRequestKind.Other,
        Guid? equipmentId = null,
        Guid? specId = null,
        long id = 1) =>
        new()
        {
            Id = id,
            Phone = "+7000",
            Body = "test",
            InitiatorName = "Initiator",
            ExecutorName = "Executor",
            SubmittedAt = DateTimeOffset.UtcNow,
            DueAt = DateTimeOffset.UtcNow.AddDays(1),
            Status = ServiceRequestStatus.Submitted,
            Kind = kind,
            EquipmentAssetId = equipmentId,
            SpecialVehicleUnitId = specId
        };

    public static EquipmentAsset FreeEquipment(Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Насос",
            InventoryNumber = "INV-1",
            Availability = EquipmentAvailability.Free,
            CreatedAtUtc = DateTime.UtcNow
        };

    public static SpecialVehicleUnit FreeSpec(Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Name = "Кран",
            Availability = SpecialVehicleAvailability.Free,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
}
