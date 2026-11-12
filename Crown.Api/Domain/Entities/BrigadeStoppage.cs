namespace Crown.Domain.Entities;

/// <summary>Остановка бригады (строка «Анализ остановок»). Бригада = пользователь Department с CanViewBrigadeRating.</summary>
public sealed class BrigadeStoppage
{
    public Guid Id { get; set; }

    /// <summary>Подразделение-бригада (AspNetUsers).</summary>
    public Guid BrigadeUserId { get; set; }

    /// <summary>Отчётные сутки.</summary>
    public DateOnly ReportDate { get; set; }

    /// <summary>Снимок названия бригады (DisplayName / UserName на момент записи).</summary>
    public string BrigadeName { get; set; } = string.Empty;

    /// <summary>Снимок ФИО мастера (обычно тот же DisplayName).</summary>
    public string MasterName { get; set; } = string.Empty;

    /// <summary>Кто остановил бригаду.</summary>
    public Guid? StoppedByUserId { get; set; }

    /// <summary>Снимок «должность + ФИО» / display name остановившего.</summary>
    public string StoppedByName { get; set; } = string.Empty;

    public TimeOnly StoppedAt { get; set; }

    public TimeOnly? RestartedAt { get; set; }

    /// <summary>Часы простоя (считаются при сохранении).</summary>
    public decimal Hours { get; set; }

    public string WorkDescription { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAt { get; set; }

    public Guid? LastModifiedByUserId { get; set; }

    public List<StoppageRemark> Remarks { get; set; } = [];
}
