using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using WerkonWebServicesRatchet.Contracts.Reminders;
using WerkonWebServicesRatchet.Domain.Entities;
using WerkonWebServicesRatchet.Features.Reminders;
using WerkonWebServicesRatchet.Infrastructure.Settings;

namespace WerkonWebServicesRatchet.Tests;

public sealed class ReminderDueTests
{
    [Fact]
    public async Task GetByDay_ReturnsOnlyRemindersInsideLocalDay()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var timeZone = TestHelpers.CreateAppTimeZone("Europe/Moscow");
        var appSettings = new AppSettingsService(dbContext, timeZone, new ConfigurationBuilder().Build());

        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        dbContext.AddRange(client, vehicle);

        // Local day 2026-03-10 in Moscow is [2026-03-09 21:00 UTC, 2026-03-10 21:00 UTC).
        var dayStartUtc = new DateTime(2026, 3, 9, 21, 0, 0, DateTimeKind.Utc);
        var insideStart = CreateReminder(vehicle.Id, dayStartUtc, "start of day");
        var insideEnd = CreateReminder(vehicle.Id, dayStartUtc.AddHours(23).AddMinutes(59), "end of day");
        var beforeDay = CreateReminder(vehicle.Id, dayStartUtc.AddMinutes(-1), "previous day");
        var nextDay = CreateReminder(vehicle.Id, dayStartUtc.AddHours(24), "next day");

        dbContext.Reminders.AddRange(insideStart, insideEnd, beforeDay, nextDay);
        await dbContext.SaveChangesAsync();

        var controller = new RemindersController(dbContext, timeZone, appSettings);

        var result = await controller.GetByDay(new DateOnly(2026, 3, 10), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var items = Assert.IsType<List<ReminderByDayItemResponse>>(ok.Value);

        Assert.Equal(2, items.Count);
        Assert.Equal(insideStart.Id, items[0].Id);
        Assert.Equal(insideEnd.Id, items[1].Id);
    }

    [Fact]
    public async Task Create_StoresReminderAtLocalStartOfDayInUtc()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var timeZone = TestHelpers.CreateAppTimeZone("Europe/Moscow");
        var appSettings = new AppSettingsService(dbContext, timeZone, new ConfigurationBuilder().Build());

        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        dbContext.AddRange(client, vehicle);
        await dbContext.SaveChangesAsync();

        var controller = new RemindersController(dbContext, timeZone, appSettings);

        var result = await controller.Create(
            new SaveReminderRequest
            {
                VehicleId = vehicle.Id,
                ReminderDate = new DateOnly(2026, 3, 10),
                Note = "Change oil"
            },
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        var stored = Assert.Single(dbContext.Reminders);
        Assert.Equal(new DateTime(2026, 3, 9, 21, 0, 0), stored.ReminderAtUtc);
        Assert.False(stored.IsClosed);
    }

    private static Reminder CreateReminder(Guid vehicleId, DateTime reminderAtUtc, string note) => new()
    {
        Id = Guid.NewGuid(),
        VehicleId = vehicleId,
        ReminderAtUtc = reminderAtUtc,
        Note = note,
        IsClosed = false,
        CreatedAtUtc = DateTime.UtcNow
    };
}
