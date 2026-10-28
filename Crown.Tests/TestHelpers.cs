using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using WerkonWebServicesRatchet.Domain.Entities;
using WerkonWebServicesRatchet.Infrastructure.Persistence;
using WerkonWebServicesRatchet.Infrastructure.Settings;
using WerkonWebServicesRatchet.Infrastructure.Time;

namespace WerkonWebServicesRatchet.Tests;

internal static class TestHelpers
{
    private static long _nextVisitNumber = 1000;

    public static long NextVisitNumber() => System.Threading.Interlocked.Increment(ref _nextVisitNumber);

    public static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ratchet-tests-{Guid.NewGuid():N}")
            // Controllers иногда BeginTransaction — InMemory транзакции не умеет.
            .ConfigureWarnings(w => w.Ignore(
                Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }

    public static AppTimeZone CreateAppTimeZone(string timeZoneId = "Europe/Moscow")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppTimeZone"] = timeZoneId
            })
            .Build();

        return new AppTimeZone(configuration, NullLogger<AppTimeZone>.Instance);
    }

    public static AppSettingsService CreateAppSettingsService(AppDbContext dbContext, AppTimeZone appTimeZone)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new AppSettingsService(dbContext, appTimeZone, configuration);
    }

    public static Client CreateClient(string fullName = "Test Client", string phone = "+70000000000") => new()
    {
        Id = Guid.NewGuid(),
        FullName = fullName,
        PhoneNumber = phone,
        CreatedAtUtc = DateTime.UtcNow
    };

    public static Vehicle CreateVehicle(Guid clientId) => new()
    {
        Id = Guid.NewGuid(),
        ClientId = clientId,
        Brand = "Toyota",
        Model = "Corolla",
        LicensePlate = "A123BC",
        CreatedAtUtc = DateTime.UtcNow
    };

    public static Visit CreateVisit(Guid vehicleId, DateTime? visitedAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        Number = NextVisitNumber(),
        VehicleId = vehicleId,
        VisitedAtUtc = visitedAtUtc ?? DateTime.UtcNow,
        CustomerComplaint = "Engine noise",
        Stage = DealStage.Booking,
        CreatedAtUtc = DateTime.UtcNow
    };
}
