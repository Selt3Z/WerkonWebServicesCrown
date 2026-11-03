using System.Net;
using System.Net.Http.Json;

namespace Crown.Tests.Integration;

public sealed class ApiSecurityTests : IClassFixture<CrownApiFactory>
{
    private readonly CrownApiFactory _factory;

    public ApiSecurityTests(CrownApiFactory factory) => _factory = factory;

    [Theory]
    [InlineData("/api/requests")]
    [InlineData("/api/equipment")]
    [InlineData("/api/special-vehicles")]
    [InlineData("/api/transport/board")]
    [InlineData("/api/audit-log")]
    [InlineData("/api/users")]
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

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
