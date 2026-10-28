using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using WerkonWebServicesRatchet.Contracts.Visits;
using WerkonWebServicesRatchet.Domain.Entities;
using WerkonWebServicesRatchet.Features.Visits;
using WerkonWebServicesRatchet.Infrastructure.Pdf;
using WerkonWebServicesRatchet.Infrastructure.Settings;
using WerkonWebServicesRatchet.Infrastructure.Warehouse;

namespace WerkonWebServicesRatchet.Tests;

public sealed class VisitTotalAmountTests
{
    [Fact]
    public async Task GetDetails_SumsServiceItemTotals()
    {
        await using var dbContext = TestHelpers.CreateDbContext();

        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);

        dbContext.AddRange(client, vehicle, visit);
        dbContext.VisitServiceItems.AddRange(
            new VisitServiceItem
            {
                Id = Guid.NewGuid(),
                VisitId = visit.Id,
                Name = "Oil change",
                Quantity = 2,
                UnitPrice = 150.50m,
                CreatedAtUtc = DateTime.UtcNow
            },
            new VisitServiceItem
            {
                Id = Guid.NewGuid(),
                VisitId = visit.Id,
                Name = "Air filter",
                Quantity = 1,
                UnitPrice = 99.99m,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(1)
            });
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.GetDetails(visit.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var details = Assert.IsType<VisitDetailsResponse>(ok.Value);

        Assert.Equal(2, details.ServiceItems.Count);
        Assert.Equal(301.00m, details.ServiceItems[0].TotalPrice);
        Assert.Equal(99.99m, details.ServiceItems[1].TotalPrice);
        Assert.Equal(400.99m, details.TotalAmount);
    }

    [Fact]
    public async Task GetDetails_NoServiceItems_TotalIsZero()
    {
        await using var dbContext = TestHelpers.CreateDbContext();

        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);

        dbContext.AddRange(client, vehicle, visit);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext);

        var result = await controller.GetDetails(visit.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var details = Assert.IsType<VisitDetailsResponse>(ok.Value);

        Assert.Empty(details.ServiceItems);
        Assert.Equal(0m, details.TotalAmount);
    }

    private static VisitsController CreateController(Infrastructure.Persistence.AppDbContext dbContext)
    {
        var timeZone = TestHelpers.CreateAppTimeZone();
        var configuration = new ConfigurationBuilder().Build();

        return new VisitsController(
            dbContext,
            timeZone,
            new AppSettingsService(dbContext, timeZone, configuration),
            new VisitWorkOrderPdfGenerator(),
            new WarehouseStockService(dbContext, new AppSettingsService(dbContext, timeZone, configuration)));
    }
}
