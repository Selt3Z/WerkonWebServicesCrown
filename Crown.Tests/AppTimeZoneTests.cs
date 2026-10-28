namespace WerkonWebServicesRatchet.Tests;

public sealed class AppTimeZoneTests
{
    [Fact]
    public void GetDayRangeUtc_MoscowDay_ReturnsUtcShiftedByThreeHours()
    {
        var timeZone = TestHelpers.CreateAppTimeZone("Europe/Moscow");

        var (startUtc, endUtc) = timeZone.GetDayRangeUtc(new DateOnly(2026, 1, 15));

        Assert.Equal(new DateTime(2026, 1, 14, 21, 0, 0), startUtc);
        Assert.Equal(new DateTime(2026, 1, 15, 21, 0, 0), endUtc);
        Assert.Equal(TimeSpan.FromHours(24), endUtc - startUtc);
    }

    [Fact]
    public void FromUtc_ConvertsToLocalWallClock()
    {
        var timeZone = TestHelpers.CreateAppTimeZone("Europe/Moscow");

        var local = timeZone.FromUtc(new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc));

        Assert.Equal(new DateTime(2026, 1, 15, 13, 30, 0), local);
    }

    [Fact]
    public void ToUtc_RoundTripsWithFromUtc()
    {
        var timeZone = TestHelpers.CreateAppTimeZone("Europe/Moscow");
        var localWallClock = new DateTime(2026, 6, 1, 9, 0, 0);

        var utc = timeZone.ToUtc(localWallClock);
        var backToLocal = timeZone.FromUtc(utc);

        Assert.Equal(localWallClock, backToLocal);
    }

    [Fact]
    public void ToUtcStartOfDay_MatchesDayRangeStart()
    {
        var timeZone = TestHelpers.CreateAppTimeZone("Europe/Moscow");
        var date = new DateOnly(2026, 3, 10);

        Assert.Equal(timeZone.GetDayRangeUtc(date).StartUtc, timeZone.ToUtcStartOfDay(date));
    }

    [Fact]
    public void UnknownTimeZoneId_FallsBackToServerLocal()
    {
        var timeZone = TestHelpers.CreateAppTimeZone("Not/A_Real_Zone");

        Assert.Equal(TimeZoneInfo.Local.Id, timeZone.TimeZoneId);
    }

    [Fact]
    public void SetTimeZoneId_ChangesActiveTimeZone()
    {
        var timeZone = TestHelpers.CreateAppTimeZone("Europe/Moscow");

        timeZone.SetTimeZoneId("Asia/Tokyo");

        var local = timeZone.FromUtc(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal(new DateTime(2026, 1, 15, 9, 0, 0), local);
    }
}
