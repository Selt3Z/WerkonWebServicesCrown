using WerkonWebServicesRatchet.Domain.Entities;
using WerkonWebServicesRatchet.Features.Summary;

namespace WerkonWebServicesRatchet.Tests;

public sealed class SummaryReportServiceTests
{
    [Fact]
    public async Task BuildAsync_CountsVisitsByStatusAndReturningClients()
    {
        await using var dbContext = TestHelpers.CreateDbContext();
        var appTimeZone = TestHelpers.CreateAppTimeZone();
        var today = appTimeZone.GetToday();
        var (startUtc, endUtc) = appTimeZone.GetDayRangeUtc(today);

        var returningClient = TestHelpers.CreateClient();
        returningClient.CreatedAtUtc = startUtc.AddDays(-30);
        var newClient = TestHelpers.CreateClient();
        newClient.CreatedAtUtc = startUtc;

        var returningVehicle = TestHelpers.CreateVehicle(returningClient.Id);
        var newVehicle = TestHelpers.CreateVehicle(newClient.Id);

        var priorVisit = TestHelpers.CreateVisit(returningVehicle.Id, startUtc.AddDays(-10));
        priorVisit.Stage = DealStage.Won;

        var createdVisit = TestHelpers.CreateVisit(returningVehicle.Id, startUtc.AddHours(2));
        createdVisit.Stage = DealStage.Booking;

        var inProgressVisit = TestHelpers.CreateVisit(newVehicle.Id, startUtc.AddHours(3));
        inProgressVisit.Stage = DealStage.Order;

        var completedVisit = TestHelpers.CreateVisit(returningVehicle.Id, startUtc.AddHours(4));
        completedVisit.Stage = DealStage.Won;

        var canceledVisit = TestHelpers.CreateVisit(newVehicle.Id, startUtc.AddHours(5));
        canceledVisit.Stage = DealStage.Lost;

        dbContext.AddRange(
            returningClient,
            newClient,
            returningVehicle,
            newVehicle,
            priorVisit,
            createdVisit,
            inProgressVisit,
            completedVisit,
            canceledVisit);
        await dbContext.SaveChangesAsync();

        var service = new SummaryReportService(
            dbContext,
            appTimeZone,
            TestHelpers.CreateAppSettingsService(dbContext, appTimeZone));
        var summary = await service.BuildAsync("today", year: null, month: null);

        Assert.Equal(4, summary.TotalVisitCount);
        Assert.Equal(1, summary.VisitStatusBreakdown.CreatedCount);
        Assert.Equal(1, summary.VisitStatusBreakdown.InProgressCount);
        Assert.Equal(1, summary.VisitStatusBreakdown.CompletedCount);
        Assert.Equal(1, summary.VisitStatusBreakdown.CanceledCount);
        Assert.Equal(1, summary.CompletedVisitCount);
        Assert.Equal(25m, summary.VisitCompletionRatePercent);
        Assert.Equal(1, summary.NewClientCount);
        Assert.Equal(1, summary.ReturningClientCount);
    }
}
