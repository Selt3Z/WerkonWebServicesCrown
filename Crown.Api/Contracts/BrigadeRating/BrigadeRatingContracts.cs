namespace Crown.Contracts.BrigadeRating;

/// <summary>Участник рейтинга = Department с CanViewBrigadeRating.</summary>
public sealed class BrigadeParticipantResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
}

public sealed class StoppageActItemResponse
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SaveStoppageActItemRequest
{
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class StoppageWorkItemResponse
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
}

public sealed class SaveStoppageWorkItemRequest
{
    public string Text { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public sealed class StoppageRemarkResponse
{
    public Guid Id { get; set; }
    public Guid? ActItemId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsManual { get; set; }
    public bool IsResolved { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public int SortOrder { get; set; }
}

public class BrigadeStoppageListItemResponse
{
    public Guid Id { get; set; }
    public DateOnly ReportDate { get; set; }
    public Guid BrigadeUserId { get; set; }
    public string BrigadeName { get; set; } = string.Empty;
    public string MasterName { get; set; } = string.Empty;
    public Guid? MasterPersonId { get; set; }
    public string StoppedByName { get; set; } = string.Empty;
    public Guid? StoppedByUserId { get; set; }
    public Guid? StoppedByPersonId { get; set; }
    public TimeOnly StoppedAt { get; set; }
    public TimeOnly? RestartedAt { get; set; }
    public decimal Hours { get; set; }
    public string WorkDescription { get; set; } = string.Empty;
    public Guid? WorkItemId { get; set; }
    public bool IsWorkManual { get; set; } = true;
    public int RemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
}

public sealed class BrigadeStoppageDetailResponse : BrigadeStoppageListItemResponse
{
    public List<StoppageRemarkResponse> Remarks { get; set; } = [];
}

public sealed class SaveStoppageRemarkRequest
{
    public Guid? Id { get; set; }
    public Guid? ActItemId { get; set; }
    /// <summary>Ручной текст (если IsManual / без ActItemId).</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Комментарий к пункту из справочника.</summary>
    public string Comment { get; set; } = string.Empty;
    public bool IsManual { get; set; }
    public bool IsResolved { get; set; }
}

public sealed class SaveBrigadeStoppageRequest
{
    public Guid BrigadeUserId { get; set; }
    public DateOnly ReportDate { get; set; }
    public string? MasterName { get; set; }
    /// <summary>Мастер из персонала бригады; MasterName берётся из FullName.</summary>
    public Guid? MasterPersonId { get; set; }
    public Guid? StoppedByUserId { get; set; }
    /// <summary>Кто остановил из персонала бригады; StoppedByName = FullName.</summary>
    public Guid? StoppedByPersonId { get; set; }
    public string? StoppedByName { get; set; }
    public TimeOnly StoppedAt { get; set; }
    public TimeOnly? RestartedAt { get; set; }
    /// <summary>Ручной текст работы (если IsWorkManual).</summary>
    public string WorkDescription { get; set; } = string.Empty;
    public Guid? WorkItemId { get; set; }
    public bool IsWorkManual { get; set; } = true;
    public List<SaveStoppageRemarkRequest> Remarks { get; set; } = [];
}

public sealed class SetRemarkResolvedRequest
{
    public bool IsResolved { get; set; }
}

public sealed class BrigadeRatingDashboardResponse
{
    /// <summary>today | month | year | custom</summary>
    public string Period { get; set; } = "month";

    /// <summary>Год самой старой остановки в области видимости (для селекта).</summary>
    public int EarliestYear { get; set; }

    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int StoppageCount { get; set; }
    public int RemarkCount { get; set; }
    public int ResolvedRemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
    public decimal TotalHours { get; set; }

    /// <summary>Доля устранённых пунктов, 0–100.</summary>
    public decimal ResolutionRatePercent { get; set; }

    /// <summary>Средняя длительность остановки (часы), только со временем пуска.</summary>
    public decimal AverageHours { get; set; }

    public BrigadeRatingPeriodComparisonResponse PeriodComparison { get; set; } = new();
    public List<BrigadeDailyTrendPointResponse> DailyTrend { get; set; } = [];
    public List<StopperTopItemResponse> TopStoppers { get; set; } = [];
    public List<BrigadeRankItemResponse> BrigadeRanking { get; set; } = [];
    public List<RemarkTopItemResponse> TopRemarks { get; set; } = [];
    public List<WorkTopItemResponse> TopWorks { get; set; } = [];
    public List<BrigadeStoppageListItemResponse> Stoppages { get; set; } = [];
}

public sealed class BrigadeRatingPeriodComparisonResponse
{
    public DateOnly PreviousPeriodStart { get; set; }
    public DateOnly PreviousPeriodEnd { get; set; }
    public int PreviousStoppageCount { get; set; }
    public int PreviousRemarkCount { get; set; }
    public int PreviousResolvedRemarkCount { get; set; }
    public int PreviousUnresolvedRemarkCount { get; set; }
    public decimal PreviousTotalHours { get; set; }
    public decimal PreviousResolutionRatePercent { get; set; }
    public decimal PreviousAverageHours { get; set; }
}

public sealed class BrigadeDailyTrendPointResponse
{
    public DateOnly Date { get; set; }
    public string Label { get; set; } = string.Empty;
    public int StoppageCount { get; set; }
    public decimal Hours { get; set; }
    public int RemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
}

public sealed class StopperTopItemResponse
{
    public string Name { get; set; } = string.Empty;
    public int StoppageCount { get; set; }
    public decimal TotalHours { get; set; }
}

public sealed class BrigadeRankItemResponse
{
    public Guid BrigadeUserId { get; set; }
    public string BrigadeName { get; set; } = string.Empty;
    public string MasterName { get; set; } = string.Empty;
    public int StoppageCount { get; set; }
    public int RemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
    public decimal TotalHours { get; set; }
    /// <summary>Меньше = лучше (для сортировки рейтинга).</summary>
    public decimal Score { get; set; }
}

public sealed class RemarkTopItemResponse
{
    public string Text { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class WorkTopItemResponse
{
    public string Text { get; set; } = string.Empty;
    public int Count { get; set; }
}
