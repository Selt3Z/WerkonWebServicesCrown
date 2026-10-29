namespace Crown.Contracts.Summary;

public sealed class SummaryResponse
{
    public string Period { get; set; } = "today";

    public DateOnly PeriodStart { get; set; }

    public DateOnly PeriodEnd { get; set; }

    public Guid UserId { get; set; }

    public string UserDisplayName { get; set; } = string.Empty;

    /// <summary>Админ/диспетчер могут переключать пользователя; иначе null.</summary>
    public List<SummaryUserOptionResponse>? AvailableUsers { get; set; }

    public int OutgoingCount { get; set; }

    public int IncomingCount { get; set; }

    public int TotalCount { get; set; }

    public int CompletedOutgoingCount { get; set; }

    public int CompletedIncomingCount { get; set; }

    public int CompletedTotalCount { get; set; }

    public decimal CompletionRatePercent { get; set; }

    public decimal OutgoingSharePercent { get; set; }

    public decimal IncomingSharePercent { get; set; }

    /// <summary>Среднее время реакции исполнителя (часы), null если нечего считать.</summary>
    public decimal? AverageReactionHours { get; set; }

    /// <summary>Снимок «сейчас», не привязан к периоду.</summary>
    public int OpenOutgoingCount { get; set; }

    public int OpenIncomingCount { get; set; }

    public int OverdueOutgoingCount { get; set; }

    public int OverdueIncomingCount { get; set; }

    public int UnacknowledgedIncomingCount { get; set; }

    public SummaryStatusBreakdownResponse OutgoingStatusBreakdown { get; set; } = new();

    public SummaryStatusBreakdownResponse IncomingStatusBreakdown { get; set; } = new();

    public SummaryPeriodComparisonResponse PeriodComparison { get; set; } = new();

    public List<SummaryDailyTrendItemResponse> DailyTrend { get; set; } = [];
}

public sealed class SummaryUserOptionResponse
{
    public Guid Id { get; set; }

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

public sealed class SummaryStatusBreakdownResponse
{
    public int SubmittedCount { get; set; }

    public int InProgressCount { get; set; }

    public int ReturnedCount { get; set; }

    public int RejectedCount { get; set; }

    public int CompletedCount { get; set; }

    public int CancelledCount { get; set; }
}

public sealed class SummaryPeriodComparisonResponse
{
    public DateOnly PreviousPeriodStart { get; set; }

    public DateOnly PreviousPeriodEnd { get; set; }

    public int PreviousOutgoingCount { get; set; }

    public int PreviousIncomingCount { get; set; }

    public int PreviousTotalCount { get; set; }

    public int PreviousCompletedTotalCount { get; set; }

    public decimal PreviousCompletionRatePercent { get; set; }
}

public sealed class SummaryDailyTrendItemResponse
{
    public DateOnly Date { get; set; }

    public string Label { get; set; } = string.Empty;

    public int SubmittedCount { get; set; }

    public int CompletedCount { get; set; }
}
