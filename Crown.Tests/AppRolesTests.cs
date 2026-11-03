using Crown.Infrastructure.Identity;

namespace Crown.Tests;

public sealed class AppRolesTests
{
    [Theory]
    [InlineData(AppRoles.Administrator, true)]
    [InlineData(AppRoles.Dispatcher, true)]
    [InlineData(AppRoles.Department, false)]
    [InlineData(AppRoles.InitiatorLegacy, false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsDispatcherLikeRole_MatchesAdminAndDispatcher(string? role, bool expected) =>
        Assert.Equal(expected, AppRoles.IsDispatcherLikeRole(role));

    [Fact]
    public void BusinessUsers_ContainsDepartmentDispatcherAdmin()
    {
        Assert.Contains(AppRoles.Administrator, AppRoles.BusinessUsers);
        Assert.Contains(AppRoles.Dispatcher, AppRoles.BusinessUsers);
        Assert.Contains(AppRoles.Department, AppRoles.BusinessUsers);
        Assert.DoesNotContain(AppRoles.InitiatorLegacy, AppRoles.BusinessUsers);
    }

    [Fact]
    public void CanManageUsers_OnlyAdministrator()
    {
        Assert.Single(AppRoles.CanManageUsers);
        Assert.Equal(AppRoles.Administrator, AppRoles.CanManageUsers[0]);
    }
}
