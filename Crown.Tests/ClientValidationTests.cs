using Microsoft.AspNetCore.Mvc;
using WerkonWebServicesRatchet.Contracts.Clients;
using WerkonWebServicesRatchet.Features.Clients;

namespace WerkonWebServicesRatchet.Tests;

public sealed class ClientValidationTests
{
    [Fact]
    public async Task Create_MissingName_ReturnsValidationProblem()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Create(
            new SaveClientRequest { FullName = " ", PhoneNumber = "" },
            culture: "ru-RU",
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);

        Assert.Contains(nameof(SaveClientRequest.FullName), problem.Errors.Keys);
        Assert.DoesNotContain(nameof(SaveClientRequest.PhoneNumber), problem.Errors.Keys);
        Assert.Empty(dbContext.Clients);
    }

    [Fact]
    public async Task Create_MissingPhone_AllowsEmptyPhone()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Create(
            new SaveClientRequest { FullName = "Walk-in", PhoneNumber = "" },
            culture: "ru-RU",
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        var stored = Assert.Single(dbContext.Clients);
        Assert.Equal("Walk-in", stored.FullName);
        Assert.Equal(string.Empty, stored.PhoneNumber);
        Assert.NotNull(stored.RegisteredDate);
        Assert.NotNull(stored.RegisteredTime);
    }

    [Fact]
    public async Task Create_ValidRequest_TrimsAndStoresClient()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Create(
            new SaveClientRequest
            {
                FullName = "  Ivan Petrov  ",
                PhoneNumber = " +79991234567 ",
                Notes = "   "
            },
            culture: "ru-RU",
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);

        var stored = Assert.Single(dbContext.Clients);
        Assert.Equal("Ivan Petrov", stored.FullName);
        Assert.Equal("+79991234567", stored.PhoneNumber);
        Assert.Null(stored.Inn);
        Assert.Null(stored.Notes);
        Assert.False(stored.IsArchived);
    }

    [Fact]
    public async Task Create_WithValidInn_StoresInn()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Create(
            new SaveClientRequest
            {
                FullName = "ООО Ромашка",
                PhoneNumber = "+79991234567",
                Inn = "7707083893"
            },
            culture: "ru-RU",
            CancellationToken.None);

        Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal("7707083893", Assert.Single(dbContext.Clients).Inn);
    }

    [Fact]
    public async Task Create_InvalidInn_ReturnsValidationProblem()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Create(
            new SaveClientRequest
            {
                FullName = "Org",
                PhoneNumber = "+79991234567",
                Inn = "123"
            },
            culture: "ru-RU",
            CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        var problem = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains(nameof(SaveClientRequest.Inn), problem.Errors.Keys);
        Assert.Empty(dbContext.Clients);
    }

    [Fact]
    public async Task Update_MissingPhone_ClearsPhone()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var client = TestHelpers.CreateClient();
        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync();

        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Update(
            client.Id,
            new SaveClientRequest { FullName = "New Name", PhoneNumber = "" },
            culture: "ru-RU",
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(string.Empty, client.PhoneNumber);
        Assert.Equal("New Name", client.FullName);
    }

    [Fact]
    public async Task Create_DuplicatePhone_ReturnsConflictWithExistingClientId()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var existing = TestHelpers.CreateClient(fullName: "Existing", phone: "+79991234567");
        dbContext.Clients.Add(existing);
        await dbContext.SaveChangesAsync();

        var controller = new ClientsController(dbContext, TestHelpers.CreateAppTimeZone());

        var result = await controller.Create(
            new SaveClientRequest
            {
                FullName = "Another",
                PhoneNumber = "+7 (999) 123-45-67"
            },
            culture: "ru-RU",
            CancellationToken.None);

        var conflict = Assert.IsType<ConflictObjectResult>(result.Result);
        var body = Assert.IsType<ClientPhoneExistsResponse>(conflict.Value);
        Assert.True(body.Exists);
        Assert.Equal(existing.Id, body.ExistingClientId);
        Assert.Equal(1, dbContext.Clients.Count());
    }
}
