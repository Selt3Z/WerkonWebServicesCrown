using System.Net;
using System.Net.Http.Json;

namespace WerkonWebServicesRatchet.Tests.Integration;

public sealed class ApiSecurityTests : IClassFixture<RatchetApiFactory>
{
    private readonly RatchetApiFactory _factory;

    public ApiSecurityTests(RatchetApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/visits/by-day")]
    [InlineData("/api/reminders/by-day")]
    [InlineData("/api/catalog-services")]
    [InlineData("/api/audit-log")]
    public async Task ProtectedEndpoints_WithoutAuth_Return401(string url)
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithUnknownUser_Fails()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            userName = "ghost",
            password = "WrongPassword123!"
        });

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
