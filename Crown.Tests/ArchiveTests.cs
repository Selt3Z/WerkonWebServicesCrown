using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WerkonWebServicesRatchet.Contracts.Clients;
using WerkonWebServicesRatchet.Contracts.Common;
using WerkonWebServicesRatchet.Features.Clients;
using WerkonWebServicesRatchet.Infrastructure.Audit;

namespace WerkonWebServicesRatchet.Tests;

public sealed class ArchiveTests
{
    [Fact]
    public async Task Archive_HidesClientFromDefaultList()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync();

        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var archiveResult = await controller.Archive(client.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(archiveResult);

        var defaultList = await GetAllAsync(controller, includeArchived: false);
        Assert.Empty(defaultList.Items);

        var fullList = await GetAllAsync(controller, includeArchived: true);
        var item = Assert.Single(fullList.Items);
        Assert.True(item.IsArchived);
    }

    [Fact]
    public async Task Restore_BringsClientBackToDefaultList()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        client.IsArchived = true;
        client.ArchivedAtUtc = DateTime.UtcNow;
        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync();

        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var restoreResult = await controller.Restore(client.Id, CancellationToken.None);
        Assert.IsType<NoContentResult>(restoreResult);

        var defaultList = await GetAllAsync(controller, includeArchived: false);
        var item = Assert.Single(defaultList.Items);
        Assert.False(item.IsArchived);

        var stored = await dbContext.Clients.SingleAsync(x => x.Id == client.Id);
        Assert.Null(stored.ArchivedAtUtc);
    }

    [Fact]
    public async Task GetById_ReturnsArchivedClient()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        client.IsArchived = true;
        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync();

        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.GetById(client.Id, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<ClientResponse>(ok.Value);
        Assert.True(response.IsArchived);
    }

    [Fact]
    public async Task Delete_ClientWithVehicles_ReturnsConflict()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        var vehicle = TestHelpers.CreateVehicle(client.Id);
        dbContext.AddRange(client, vehicle);
        await dbContext.SaveChangesAsync();

        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Delete(client.Id, CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Single(await dbContext.Clients.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Delete_EmptyClient_RemovesIt()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync();

        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Delete(client.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await dbContext.Clients.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task AuditEntryFactory_MapsArchiveAndRestoreActions()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync();

        var factory = new AuditEntryFactory(TestHelpers.CreateAppTimeZone());

        client.IsArchived = true;
        dbContext.ChangeTracker.DetectChanges();

        var archiveEntries = await factory.CreateEntriesAsync(
            dbContext,
            dbContext.ChangeTracker.Entries().ToList(),
            userId: null,
            userDisplayName: "tester",
            CancellationToken.None);

        var archived = Assert.Single(archiveEntries);
        Assert.Equal(AuditActions.Archived, archived.Action);
        await dbContext.SaveChangesAsync();

        client.IsArchived = false;
        dbContext.ChangeTracker.DetectChanges();

        var restoreEntries = await factory.CreateEntriesAsync(
            dbContext,
            dbContext.ChangeTracker.Entries().ToList(),
            userId: null,
            userDisplayName: "tester",
            CancellationToken.None);

        var restored = Assert.Single(restoreEntries);
        Assert.Equal(AuditActions.Restored, restored.Action);
    }

    private static async Task<PagedResponse<ClientResponse>> GetAllAsync(
        ClientsController controller,
        bool includeArchived)
    {
        var result = await controller.GetAll(
            name: null,
            phone: null,
            includeArchived: includeArchived,
            skip: null,
            take: null,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<PagedResponse<ClientResponse>>(ok.Value);
    }
}
