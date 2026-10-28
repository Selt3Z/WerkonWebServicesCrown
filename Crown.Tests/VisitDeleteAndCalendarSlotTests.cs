using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using WerkonWebServicesRatchet.Contracts.Visits;
using WerkonWebServicesRatchet.Domain.Entities;
using WerkonWebServicesRatchet.Features.Visits;
using WerkonWebServicesRatchet.Infrastructure.Audit;
using WerkonWebServicesRatchet.Infrastructure.Identity;
using WerkonWebServicesRatchet.Infrastructure.Pdf;
using WerkonWebServicesRatchet.Infrastructure.Persistence;
using WerkonWebServicesRatchet.Infrastructure.Settings;
using WerkonWebServicesRatchet.Infrastructure.Time;
using WerkonWebServicesRatchet.Infrastructure.Warehouse;

namespace WerkonWebServicesRatchet.Tests;

/// <summary>
/// Регрессии после undated deals: hard-delete + audit на DateTime?, удаление primary-слота.
/// </summary>
public sealed class VisitDeleteAndCalendarSlotTests
{
    [Fact]
    public async Task AuditFactory_DeletedVisit_WithNullableVisitedAtUtc_DoesNotThrow()
    {
        await using var db = CreateDbWithAudit(out var interceptor);
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);
        visit.VisitedAtUtc = DateTime.UtcNow;

        db.AddRange(client, vehicle, visit);
        await db.SaveChangesAsync();

        db.Visits.Remove(visit);

        // Раньше: InvalidCastException в GetDateTimeValue(DateTime) при OriginalValues VisitedAtUtc?.
        var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        Assert.Null(ex);
        Assert.False(await db.Visits.IgnoreQueryFilters().AnyAsync(x => x.Id == visit.Id));
    }

    [Fact]
    public async Task AuditFactory_DeletedVisit_WithNullVisitedAtUtc_UsesCreatedAtFallback()
    {
        await using var db = CreateDbWithAudit(out _);
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);
        visit.VisitedAtUtc = null;

        db.AddRange(client, vehicle, visit);
        await db.SaveChangesAsync();

        db.Visits.Remove(visit);
        var ex = await Record.ExceptionAsync(() => db.SaveChangesAsync());
        Assert.Null(ex);
    }

    [Fact]
    public async Task DeleteCalendarSlot_PrimaryOnlySlot_ClearsVisitedAtUtc()
    {
        await using var db = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);
        var starts = DateTime.UtcNow.AddHours(2);
        visit.VisitedAtUtc = starts;
        visit.DurationMinutes = 60;

        var slot = new VisitCalendarSlot
        {
            Id = Guid.NewGuid(),
            VisitId = visit.Id,
            StartsAtUtc = starts,
            DurationMinutes = 60,
            ServicePostCode = "B"
        };

        db.AddRange(client, vehicle, visit, slot);
        await db.SaveChangesAsync();

        var controller = CreateVisitsController(db);
        var result = await controller.DeleteCalendarSlot(visit.Id, slot.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var details = Assert.IsType<VisitDetailsResponse>(ok.Value);
        Assert.Empty(details.CalendarSlots);
        Assert.Null(details.VisitedAtUtc);

        var stored = await db.Visits.FindAsync(visit.Id);
        Assert.Null(stored!.VisitedAtUtc);
        Assert.False(await db.VisitCalendarSlots.AnyAsync(x => x.Id == slot.Id));
    }

    [Fact]
    public async Task DeleteVisit_HardDelete_WithCalendarSlot_Succeeds()
    {
        await using var db = CreateDbWithAudit(out _);
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);
        var slot = new VisitCalendarSlot
        {
            Id = Guid.NewGuid(),
            VisitId = visit.Id,
            StartsAtUtc = visit.VisitedAtUtc!.Value,
            DurationMinutes = 60
        };

        db.AddRange(client, vehicle, visit, slot);
        await db.SaveChangesAsync();

        var controller = CreateVisitsController(db);
        var result = await controller.Delete(visit.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(await db.Visits.IgnoreQueryFilters().AnyAsync(x => x.Id == visit.Id));
        Assert.False(await db.VisitCalendarSlots.AnyAsync(x => x.Id == slot.Id));
    }

    [Fact]
    public async Task BindVisitToVehicle_UndatedUnattached_DoesNotCreateSlot()
    {
        await using var db = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var unattached = new UnattachedVisit
        {
            Id = Guid.NewGuid(),
            Number = TestHelpers.NextVisitNumber(),
            VisitedAtUtc = null,
            CustomerComplaint = "undated",
            Stage = DealStage.Unsorted,
            DurationMinutes = 60,
            CreatedAtUtc = DateTime.UtcNow
        };

        db.AddRange(client, vehicle, unattached);
        await db.SaveChangesAsync();

        var controller = CreateVisitsController(db);
        var result = await controller.BindVisitToVehicle(
            unattached.Id,
            new BindVisitToVehicleRequest { VehicleId = vehicle.Id },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var details = Assert.IsType<VisitDetailsResponse>(ok.Value);
        Assert.False(details.IsUnattached);
        Assert.Equal(unattached.Number, details.Number);
        Assert.Null(details.VisitedAtUtc);
        Assert.Empty(details.CalendarSlots);
        Assert.False(await db.VisitCalendarSlots.AnyAsync(x => x.VisitId == unattached.Id));
    }

    [Fact]
    public async Task BindVisitToVehicle_WithMultipleSlots_ReparentsAllSlots()
    {
        await using var db = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var start1 = DateTime.UtcNow.Date.AddHours(9);
        var start2 = start1.AddHours(3);
        var unattached = new UnattachedVisit
        {
            Id = Guid.NewGuid(),
            Number = TestHelpers.NextVisitNumber(),
            VisitedAtUtc = start1,
            CustomerComplaint = "multi-slot",
            Stage = DealStage.Booking,
            DurationMinutes = 25,
            ServiceBayNumber = 1,
            ServicePostCode = "A",
            CreatedAtUtc = DateTime.UtcNow
        };
        var slot1 = new VisitCalendarSlot
        {
            Id = Guid.NewGuid(),
            UnattachedVisitId = unattached.Id,
            StartsAtUtc = start1,
            DurationMinutes = 25,
            ServiceBayNumber = 1,
            ServicePostCode = "A"
        };
        var slot2 = new VisitCalendarSlot
        {
            Id = Guid.NewGuid(),
            UnattachedVisitId = unattached.Id,
            StartsAtUtc = start2,
            DurationMinutes = 60,
            ServiceBayNumber = 1,
            ServicePostCode = "B"
        };

        db.AddRange(client, vehicle, unattached, slot1, slot2);
        await db.SaveChangesAsync();

        var controller = CreateVisitsController(db);
        var result = await controller.BindVisitToVehicle(
            unattached.Id,
            new BindVisitToVehicleRequest { VehicleId = vehicle.Id },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var details = Assert.IsType<VisitDetailsResponse>(ok.Value);
        Assert.False(details.IsUnattached);
        Assert.Equal(2, details.CalendarSlots.Count);
        Assert.Equal(2, await db.VisitCalendarSlots.CountAsync(x => x.VisitId == unattached.Id));
        Assert.False(await db.VisitCalendarSlots.AnyAsync(x => x.UnattachedVisitId == unattached.Id));
        Assert.False(await db.UnattachedVisits.AnyAsync(x => x.Id == unattached.Id));
    }

    [Fact]
    public async Task DeleteVisit_WithServiceItems_RemovesItemsAndVisit()
    {
        await using var db = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);
        var item = new VisitServiceItem
        {
            Id = Guid.NewGuid(),
            VisitId = visit.Id,
            Name = "Oil",
            Quantity = 1,
            UnitPrice = 100,
            DefaultUnit = "шт",
            CreatedAtUtc = DateTime.UtcNow
        };

        db.AddRange(client, vehicle, visit, item);
        await db.SaveChangesAsync();

        var controller = CreateVisitsController(db);
        var result = await controller.Delete(visit.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(result);
        Assert.False(await db.Visits.IgnoreQueryFilters().AnyAsync(x => x.Id == visit.Id));
        Assert.False(await db.VisitServiceItems.AnyAsync(x => x.VisitId == visit.Id));
    }

    private static AppDbContext CreateDbWithAudit(out AuditSaveChangesInterceptor interceptor)
    {
        var http = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var factory = new AuditEntryFactory(TestHelpers.CreateAppTimeZone());
        interceptor = new AuditSaveChangesInterceptor(http, factory);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ratchet-audit-{Guid.NewGuid():N}")
            .AddInterceptors(interceptor)
            .Options;

        return new AppDbContext(options);
    }

    private static VisitsController CreateVisitsController(AppDbContext dbContext)
    {
        var tz = TestHelpers.CreateAppTimeZone();
        var settings = new AppSettingsService(dbContext, tz, new ConfigurationBuilder().Build());
        var controller = new VisitsController(
            dbContext,
            tz,
            settings,
            new VisitWorkOrderPdfGenerator(),
            new WarehouseStockService(dbContext, settings));

        // AssignVisitMechanic policy на delete slot — в unit-тесте авторизацию не гоняем.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                    [
                        new System.Security.Claims.Claim(
                            System.Security.Claims.ClaimTypes.NameIdentifier,
                            Guid.NewGuid().ToString()),
                        new System.Security.Claims.Claim(
                            System.Security.Claims.ClaimTypes.Role,
                            AppRoles.Administrator)
                    ],
                    "test"))
            }
        };

        return controller;
    }
}
