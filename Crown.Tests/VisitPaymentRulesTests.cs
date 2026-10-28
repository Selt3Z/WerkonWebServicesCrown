using WerkonWebServicesRatchet.Domain.Entities;

namespace WerkonWebServicesRatchet.Tests;

public sealed class VisitPaymentRulesTests
{
    [Fact]
    public void AfterLineItemsChange_PaidWithZeroAmount_BecomesUnpaidWhenTotalGrows()
    {
        var visit = new UnattachedVisit
        {
            Id = Guid.NewGuid(),
            Number = 1,
            PaymentStatus = VisitPaymentStatus.Paid,
            AmountPaid = 0m,
            DurationMinutes = 60,
            CreatedAtUtc = DateTime.UtcNow
        };

        VisitPaymentRules.RecalculatePaymentStatusAfterLineItemsChange(visit, total: 1500m);

        Assert.Equal(VisitPaymentStatus.Unpaid, visit.PaymentStatus);
        Assert.Equal(0m, visit.AmountPaid);
    }

    [Fact]
    public void AfterLineItemsChange_PaidWithPartialAmount_BecomesPartialWhenTotalGrows()
    {
        var visit = new UnattachedVisit
        {
            Id = Guid.NewGuid(),
            Number = 1,
            PaymentStatus = VisitPaymentStatus.Paid,
            AmountPaid = 500m,
            DurationMinutes = 60,
            CreatedAtUtc = DateTime.UtcNow
        };

        VisitPaymentRules.RecalculatePaymentStatusAfterLineItemsChange(visit, total: 1500m);

        Assert.Equal(VisitPaymentStatus.Partial, visit.PaymentStatus);
        Assert.Equal(500m, visit.AmountPaid);
    }
}
