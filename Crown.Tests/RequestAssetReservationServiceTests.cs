using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Requests;

namespace Crown.Tests;

public sealed class RequestAssetReservationServiceTests
{
    [Fact]
    public async Task Reserve_Equipment_Free_SetsReserved()
    {
        await using var db = TestDb.Create();
        var asset = TestDb.FreeEquipment();
        db.EquipmentAssets.Add(asset);
        await db.SaveChangesAsync();

        var request = TestDb.Request(ServiceRequestKind.Equipment, equipmentId: asset.Id);
        var svc = new RequestAssetReservationService(db);

        var (ok, error) = await svc.TryReserveAsync(request, CancellationToken.None);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(EquipmentAvailability.Reserved, asset.Availability);
        Assert.Equal(request.Id, asset.ReservedByRequestId);
    }

    [Fact]
    public async Task Reserve_Equipment_BusyByOther_Fails()
    {
        await using var db = TestDb.Create();
        var asset = TestDb.FreeEquipment();
        asset.Availability = EquipmentAvailability.Reserved;
        asset.ReservedByRequestId = 99;
        db.EquipmentAssets.Add(asset);
        await db.SaveChangesAsync();

        var request = TestDb.Request(ServiceRequestKind.Equipment, equipmentId: asset.Id, id: 1);
        var svc = new RequestAssetReservationService(db);

        var (ok, error) = await svc.TryReserveAsync(request, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("занята", error);
    }

    [Fact]
    public async Task Reserve_Equipment_SameRequest_AllowsReReserve()
    {
        await using var db = TestDb.Create();
        var asset = TestDb.FreeEquipment();
        asset.Availability = EquipmentAvailability.Reserved;
        asset.ReservedByRequestId = 7;
        db.EquipmentAssets.Add(asset);
        await db.SaveChangesAsync();

        var request = TestDb.Request(ServiceRequestKind.Equipment, equipmentId: asset.Id, id: 7);
        var svc = new RequestAssetReservationService(db);

        var (ok, _) = await svc.TryReserveAsync(request, CancellationToken.None);
        Assert.True(ok);
    }

    [Fact]
    public async Task Reserve_Equipment_WithoutId_Fails()
    {
        await using var db = TestDb.Create();
        var request = TestDb.Request(ServiceRequestKind.Equipment);
        var svc = new RequestAssetReservationService(db);

        var (ok, error) = await svc.TryReserveAsync(request, CancellationToken.None);
        Assert.False(ok);
        Assert.Contains("единицу", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Reserve_SpecTech_Inactive_Fails()
    {
        await using var db = TestDb.Create();
        var unit = TestDb.FreeSpec();
        unit.IsActive = false;
        db.SpecialVehicleUnits.Add(unit);
        await db.SaveChangesAsync();

        var request = TestDb.Request(ServiceRequestKind.SpecTech, specId: unit.Id);
        var svc = new RequestAssetReservationService(db);

        var (ok, error) = await svc.TryReserveAsync(request, CancellationToken.None);
        Assert.False(ok);
        Assert.Contains("не найдена", error);
    }

    [Fact]
    public async Task Reserve_SpecTech_Free_SetsReserved()
    {
        await using var db = TestDb.Create();
        var unit = TestDb.FreeSpec();
        db.SpecialVehicleUnits.Add(unit);
        await db.SaveChangesAsync();

        var request = TestDb.Request(ServiceRequestKind.SpecTech, specId: unit.Id);
        var svc = new RequestAssetReservationService(db);

        var (ok, _) = await svc.TryReserveAsync(request, CancellationToken.None);
        Assert.True(ok);
        Assert.Equal(SpecialVehicleAvailability.Reserved, unit.Availability);
        Assert.Equal(request.Id, unit.ReservedByRequestId);
    }

    [Fact]
    public async Task Reserve_OtherKind_NoOpOk()
    {
        await using var db = TestDb.Create();
        var svc = new RequestAssetReservationService(db);
        var (ok, error) = await svc.TryReserveAsync(TestDb.Request(), CancellationToken.None);
        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public async Task Release_ClearsOnlyOwnReservation()
    {
        await using var db = TestDb.Create();
        var asset = TestDb.FreeEquipment();
        asset.Availability = EquipmentAvailability.Reserved;
        asset.ReservedByRequestId = 5;
        db.EquipmentAssets.Add(asset);
        await db.SaveChangesAsync();

        var svc = new RequestAssetReservationService(db);
        await svc.ReleaseAsync(
            TestDb.Request(ServiceRequestKind.Equipment, equipmentId: asset.Id, id: 5),
            CancellationToken.None);

        Assert.Equal(EquipmentAvailability.Free, asset.Availability);
        Assert.Null(asset.ReservedByRequestId);
    }

    [Fact]
    public async Task Release_DoesNotTouchForeignReservation()
    {
        await using var db = TestDb.Create();
        var asset = TestDb.FreeEquipment();
        asset.Availability = EquipmentAvailability.Reserved;
        asset.ReservedByRequestId = 5;
        db.EquipmentAssets.Add(asset);
        await db.SaveChangesAsync();

        var svc = new RequestAssetReservationService(db);
        await svc.ReleaseAsync(
            TestDb.Request(ServiceRequestKind.Equipment, equipmentId: asset.Id, id: 9),
            CancellationToken.None);

        Assert.Equal(EquipmentAvailability.Reserved, asset.Availability);
        Assert.Equal(5, asset.ReservedByRequestId);
    }

    [Fact]
    public async Task CompleteEquipmentMove_UpdatesLocationAndFrees()
    {
        await using var db = TestDb.Create();
        var asset = TestDb.FreeEquipment();
        asset.Availability = EquipmentAvailability.Reserved;
        asset.ReservedByRequestId = 3;
        asset.CurrentLocationLabel = "Склад";
        db.EquipmentAssets.Add(asset);
        await db.SaveChangesAsync();

        var initiatorId = Guid.NewGuid();
        var request = TestDb.Request(ServiceRequestKind.Equipment, equipmentId: asset.Id, id: 3);
        request.InitiatorUserId = initiatorId;
        request.InitiatorName = "Иванов";

        var svc = new RequestAssetReservationService(db);
        var (ok, _) = await svc.CompleteEquipmentMoveAsync(request, Guid.NewGuid(), "Disp", CancellationToken.None);

        Assert.True(ok);
        Assert.Equal(EquipmentAvailability.Free, asset.Availability);
        Assert.Null(asset.ReservedByRequestId);
        Assert.Equal(initiatorId, asset.CurrentLocationUserId);
        Assert.Equal("Иванов", asset.CurrentLocationLabel);
        await db.SaveChangesAsync();
        Assert.Single(db.EquipmentMovements.ToList());
    }

    [Fact]
    public async Task MarkSpecArrived_SetsInUse()
    {
        await using var db = TestDb.Create();
        var unit = TestDb.FreeSpec();
        unit.Availability = SpecialVehicleAvailability.Reserved;
        unit.ReservedByRequestId = 2;
        db.SpecialVehicleUnits.Add(unit);
        await db.SaveChangesAsync();

        var request = TestDb.Request(ServiceRequestKind.SpecTech, specId: unit.Id, id: 2);
        var svc = new RequestAssetReservationService(db);

        var (ok, _) = await svc.MarkSpecArrivedAsync(request, CancellationToken.None);
        Assert.True(ok);
        Assert.Equal(SpecialVehicleAvailability.InUse, unit.Availability);
        Assert.NotNull(request.SpecArrivedAt);
    }

    [Fact]
    public async Task MarkSpecReleased_FreesUnit()
    {
        await using var db = TestDb.Create();
        var unit = TestDb.FreeSpec();
        unit.Availability = SpecialVehicleAvailability.InUse;
        unit.ReservedByRequestId = 2;
        db.SpecialVehicleUnits.Add(unit);
        await db.SaveChangesAsync();

        var request = TestDb.Request(ServiceRequestKind.SpecTech, specId: unit.Id, id: 2);
        var svc = new RequestAssetReservationService(db);

        var (ok, _) = await svc.MarkSpecReleasedAsync(request, CancellationToken.None);
        Assert.True(ok);
        Assert.Equal(SpecialVehicleAvailability.Free, unit.Availability);
        Assert.Null(unit.ReservedByRequestId);
        Assert.NotNull(request.SpecReleasedAt);
    }
}
