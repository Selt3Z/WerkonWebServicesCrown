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
    public string StoppedByName { get; set; } = string.Empty;
    public Guid? StoppedByUserId { get; set; }
    public TimeOnly StoppedAt { get; set; }
    public TimeOnly? RestartedAt { get; set; }
    public decimal Hours { get; set; }
    public string WorkDescription { get; set; } = string.Empty;
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
    public Guid? StoppedByUserId { get; set; }
    public string? StoppedByName { get; set; }
    public TimeOnly StoppedAt { get; set; }
    public TimeOnly? RestartedAt { get; set; }
    public string WorkDescription { get; set; } = string.Empty;
    public List<SaveStoppageRemarkRequest> Remarks { get; set; } = [];
}

public sealed class SetRemarkResolvedRequest
{
    public bool IsResolved { get; set; }
}

public sealed class BrigadeRatingDashboardResponse
{
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }
    public int StoppageCount { get; set; }
    public int RemarkCount { get; set; }
    public int ResolvedRemarkCount { get; set; }
    public int UnresolvedRemarkCount { get; set; }
    public decimal TotalHours { get; set; }
    public List<BrigadeRankItemResponse> BrigadeRanking { get; set; } = [];
    public List<RemarkTopItemResponse> TopRemarks { get; set; } = [];
    public List<BrigadeStoppageListItemResponse> Stoppages { get; set; } = [];
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
