using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Features.Requests;

namespace Crown.Tests;

public sealed class ServiceRequestStatusGuardTests
{
    [Theory]
    [InlineData(ServiceRequestStatus.Submitted, "Подана")]
    [InlineData(ServiceRequestStatus.InProgress, "В работе")]
    [InlineData(ServiceRequestStatus.Rejected, "Отклонена")]
    [InlineData(ServiceRequestStatus.Returned, "Возвращена")]
    [InlineData(ServiceRequestStatus.Completed, "Выполнена")]
    [InlineData(ServiceRequestStatus.Cancelled, "Отменена")]
    public void StatusLabel_MatchesUi(ServiceRequestStatus status, string label) =>
        Assert.Equal(label, RequestsController.StatusLabel(status));

    [Theory]
    [InlineData(ServiceRequestKind.Other, "Другое")]
    [InlineData(ServiceRequestKind.Equipment, "Оборудование")]
    [InlineData(ServiceRequestKind.SpecTech, "Спецтехника")]
    public void KindLabel_MatchesUi(ServiceRequestKind kind, string label) =>
        Assert.Equal(label, RequestsController.KindLabel(kind));

    [Fact]
    public void TryEnsureStatus_AllowsListed()
    {
        var entity = new ServiceRequest { Status = ServiceRequestStatus.Submitted };
        Assert.True(RequestsController.TryEnsureStatus(
            entity, out var error, ServiceRequestStatus.Submitted, ServiceRequestStatus.Returned));
        Assert.Null(error);
    }

    [Fact]
    public void TryEnsureStatus_RejectsOthers()
    {
        var entity = new ServiceRequest { Status = ServiceRequestStatus.Completed };
        Assert.False(RequestsController.TryEnsureStatus(
            entity, out var error, ServiceRequestStatus.InProgress));
        Assert.Contains("Выполнена", error);
    }

    [Fact]
    public void AcceptTransition_FromReturned_Ok()
    {
        var entity = new ServiceRequest { Status = ServiceRequestStatus.Returned };
        Assert.True(RequestsController.TryEnsureStatus(
            entity, out _, ServiceRequestStatus.Submitted, ServiceRequestStatus.Returned));
    }

    [Fact]
    public void CompleteTransition_RequiresInProgress()
    {
        var submitted = new ServiceRequest { Status = ServiceRequestStatus.Submitted };
        Assert.False(RequestsController.TryEnsureStatus(
            submitted, out _, ServiceRequestStatus.InProgress));

        var inProgress = new ServiceRequest { Status = ServiceRequestStatus.InProgress };
        Assert.True(RequestsController.TryEnsureStatus(
            inProgress, out _, ServiceRequestStatus.InProgress));
    }
}
