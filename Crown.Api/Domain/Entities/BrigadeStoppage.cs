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

    /// <summary>Снимок ФИО мастера (из персонала или fallback).</summary>
    public string MasterName { get; set; } = string.Empty;

    /// <summary>Мастер из справочника персонала подразделения (снимок имени в MasterName).</summary>
    public Guid? MasterPersonId { get; set; }

    /// <summary>Кто остановил (логин, legacy). Предпочтительно StoppedByPersonId.</summary>
    public Guid? StoppedByUserId { get; set; }

    /// <summary>Кто остановил из персонала бригады (снимок в StoppedByName).</summary>
    public Guid? StoppedByPersonId { get; set; }

    /// <summary>Снимок ФИО / должности остановившего.</summary>
    public string StoppedByName { get; set; } = string.Empty;

    public TimeOnly StoppedAt { get; set; }

    public TimeOnly? RestartedAt { get; set; }

    /// <summary>Часы простоя (считаются при сохранении).</summary>
    public decimal Hours { get; set; }

    /// <summary>Снимок названия работы (из справочника или вручную).</summary>
    public string WorkDescription { get; set; } = string.Empty;

    public Guid? WorkItemId { get; set; }

    /// <summary>true = текст вручную, без WorkItemId.</summary>
    public bool IsWorkManual { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTimeOffset? LastModifiedAt { get; set; }

    public Guid? LastModifiedByUserId { get; set; }

    public List<StoppageRemark> Remarks { get; set; } = [];
}
