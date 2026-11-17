using Crown.Infrastructure.Identity;

namespace Crown.Tests;

public sealed class AppRolesTests
{
    [Theory]
    [InlineData(AppRoles.Administrator, true)]
    [InlineData(AppRoles.Dispatcher, true)]
    [InlineData(AppRoles.Department, false)]
    [InlineData(AppRoles.Customer, false)]
    [InlineData(AppRoles.Brigade, false)]
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
    public void All_And_BusinessUsers_ContainCustomerAndBrigade()
    {
        Assert.Contains(AppRoles.Customer, AppRoles.All);
        Assert.Contains(AppRoles.Brigade, AppRoles.All);
        Assert.Contains(AppRoles.Customer, AppRoles.BusinessUsers);
        Assert.Contains(AppRoles.Brigade, AppRoles.BusinessUsers);
    }

    [Theory]
    [InlineData(AppRoles.Department, true)]
    [InlineData(AppRoles.Customer, true)]
    [InlineData(AppRoles.Brigade, true)]
    [InlineData(AppRoles.Administrator, false)]
    [InlineData(AppRoles.Dispatcher, false)]
    [InlineData(null, false)]
    public void IsSwitchableRole_OnlyDepartmentCustomerBrigade(string? role, bool expected) =>
        Assert.Equal(expected, AppRoles.IsSwitchableRole(role));

    [Fact]
    public void CanManageUsers_OnlyAdministrator()
    {
        Assert.Single(AppRoles.CanManageUsers);
        Assert.Equal(AppRoles.Administrator, AppRoles.CanManageUsers[0]);
    }

    [Fact]
    public void FormatUserDisplay_AppendsNumberOnlyForBrigade()
    {
        var user = new ApplicationUser { UserName = "b5", DisplayName = "Бригада Иванова", BrigadeNumber = " 5 " };

        Assert.Equal("Бригада Иванова №5", UserDisplay.FormatUserDisplay(user, isBrigade: true));
        Assert.Equal("Бригада Иванова", UserDisplay.FormatUserDisplay(user, isBrigade: false));
        Assert.Equal("Бригада Иванова №5", UserDisplay.Format(user));
    }

    [Fact]
    public void FormatUserDisplay_FallsBackToUserName_AndSkipsEmptyNumber()
    {
        var user = new ApplicationUser { UserName = "b7", DisplayName = " ", BrigadeNumber = null };

        Assert.Equal("b7", UserDisplay.FormatBrigadeDisplay(user));
        Assert.Equal("b7", UserDisplay.Format(user));
    }
}
