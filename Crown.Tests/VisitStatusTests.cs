using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using WerkonWebServicesRatchet.Contracts.Visits;
using WerkonWebServicesRatchet.Domain.Entities;
using WerkonWebServicesRatchet.Features.Visits;
using WerkonWebServicesRatchet.Infrastructure.Identity;
using WerkonWebServicesRatchet.Infrastructure.Pdf;
using WerkonWebServicesRatchet.Infrastructure.Settings;
using WerkonWebServicesRatchet.Infrastructure.Warehouse;

namespace WerkonWebServicesRatchet.Tests;

public sealed class VisitStatusTests
{
    [Fact]
    public async Task UpdateStatus_MechanicStartsWork_AssignsCurrentUser()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var mechanicId = await SeedMechanicAsync(dbContext);

        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);

        dbContext.AddRange(client, vehicle, visit);
        await dbContext.SaveChangesAsync();

        var controller = CreateController(dbContext, mechanicId);

        var result = await controller.UpdateStatus(
            visit.Id,
            new UpdateVisitStatusRequest { Stage = (int)DealStage.Order },
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<VisitResponse>(ok.Value);

        Assert.Equal(DealStage.Order, response.Stage);
        Assert.Equal(mechanicId, response.AssignedMechanicUserId);

        var storedVisit = await dbContext.Visits.FindAsync(visit.Id);
        Assert.Equal(mechanicId, storedVisit!.AssignedMechanicUserId);
    }

    [Fact]
    public async Task UpdateStatus_ReopenVisit_DoesNotAutoAssignMechanic()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var mechanicId = await SeedMechanicAsync(dbContext);

        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        var visit = TestHelpers.CreateVisit(vehicle.Id);
        visit.Stage = DealStage.Won;

        dbContext.AddRange(client, vehicle, visit);
        await dbContext.SaveChangesAsync();

        // Reopen Won→Order — только manager/admin (не mechanic shortcut).
        var controller = CreateController(dbContext, mechanicId, AppRoles.Manager);

        var result = await controller.UpdateStatus(
            visit.Id,
            new UpdateVisitStatusRequest { Stage = (int)DealStage.Order },
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);

        var storedVisit = await dbContext.Visits.FindAsync(visit.Id);
        Assert.Null(storedVisit!.AssignedMechanicUserId);
    }

    private static async Task<Guid> SeedMechanicAsync(Infrastructure.Persistence.AppDbContext dbContext)
    {
        var mechanicRole = new IdentityRole<Guid>
        {
            Id = Guid.NewGuid(),
            Name = AppRoles.Mechanic,
            NormalizedName = AppRoles.Mechanic.ToUpperInvariant()
        };

        var mechanic = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "mechanic",
            NormalizedUserName = "MECHANIC",
            DisplayName = "Test Mechanic"
        };

        dbContext.Roles.Add(mechanicRole);
        dbContext.Users.Add(mechanic);
        dbContext.UserRoles.Add(new IdentityUserRole<Guid>
        {
            UserId = mechanic.Id,
            RoleId = mechanicRole.Id
        });

        await dbContext.SaveChangesAsync();
        return mechanic.Id;
    }

    private static VisitsController CreateController(
        Infrastructure.Persistence.AppDbContext dbContext,
        Guid? currentUserId = null,
        string role = AppRoles.Mechanic)
    {
        var controller = new VisitsController(
            dbContext,
            TestHelpers.CreateAppTimeZone(),
            new AppSettingsService(dbContext, TestHelpers.CreateAppTimeZone(), new ConfigurationBuilder().Build()),
            new VisitWorkOrderPdfGenerator(),
            new WarehouseStockService(
                dbContext,
                new AppSettingsService(dbContext, TestHelpers.CreateAppTimeZone(), new ConfigurationBuilder().Build())));

        if (currentUserId.HasValue)
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, currentUserId.Value.ToString()),
                new Claim(ClaimTypes.Role, role)
            ],
            authenticationType: "test");

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(identity)
                }
            };
        }

        return controller;
    }
}
