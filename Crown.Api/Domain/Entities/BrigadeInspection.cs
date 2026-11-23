namespace Crown.Domain.Entities;

/// <summary>
/// Проверка бригады (надзорная / ПДК / ПБиОТ / ППК). Одна таблица на все виды:
/// поля вида-специфичные (Outcome, FixResponsibleText, FixReportText…) заполняются только для своего Kind.
/// Имена и цех/номер — снимки на момент записи.
/// </summary>
public sealed class BrigadeInspection
{
    public Guid Id { get; set; }

    public BrigadeInspectionKind Kind { get; set; }

    /// <summary>Сквозной номер «№» внутри вида (уникален в паре Kind + SequenceNo).</summary>
    public int SequenceNo { get; set; }

    public DateOnly ReportDate { get; set; }

    /// <summary>Снимок цеха бригады (ApplicationUser.BrigadeWorkshop).</summary>
    public string Workshop { get; set; } = string.Empty;

    /// <summary>Бригада (пользователь роли Brigade).</summary>
    public Guid BrigadeUserId { get; set; }

    /// <summary>Снимок номера бригады (ApplicationUser.BrigadeNumber).</summary>
    public string BrigadeNumberSnapshot { get; set; } = string.Empty;

    /// <summary>Мастер из персонала бригады (снимок ФИО в MasterName).</summary>
    public Guid? MasterPersonId { get; set; }

    public string MasterName { get; set; } = string.Empty;

    /// <summary>Скважина (Supervising / Pbiot).</summary>
    public string? Well { get; set; }

    /// <summary>Проверяющий из персонала подразделения (снимок ФИО/должности ниже).</summary>
    public Guid? CheckerPersonId { get; set; }

    public string CheckerName { get; set; } = string.Empty;

    public string? CheckerPosition { get; set; }

    /// <summary>Дата устранения (если задана датой).</summary>
    public DateOnly? FixDate { get; set; }

    /// <summary>true = вместо даты свободный текст (FixDateManualText), например «по факту».</summary>
    public bool FixDateIsManual { get; set; }

    public string? FixDateManualText { get; set; }

    // ---------- Supervising ----------

    /// <summary>Предписание / Замечание / Остановка. Для остальных видов — null.</summary>
    public BrigadeInspectionOutcome? Outcome { get; set; }

    /// <summary>Остановочный пункт.</summary>
    public bool StopPoint { get; set; }

    /// <summary>Есть объяснительная.</summary>
    public bool HasExplanation { get; set; }

    /// <summary>Подразделение проверяющего (как StoppedByUserId в остановках).</summary>
    public Guid? DepartmentUserId { get; set; }

    /// <summary>Снимок названия подразделения проверяющего.</summary>
    public string? DepartmentName { get; set; }

    // ---------- Pdk ----------

    /// <summary>Ответственный за устранение (свободный текст).</summary>
    public string? FixResponsibleText { get; set; }

    // ---------- Pbiot ----------

    /// <summary>Отчёт об устранении.</summary>
    public string? FixReportText { get; set; }

    /// <summary>Ответственный за устранение — из персонала бригад того же цеха.</summary>
    public Guid? FixResponsiblePersonId { get; set; }

    public string? FixResponsibleName { get; set; }

    /// <summary>Свободная заметка к проверке/ППК.</summary>
    public string? Notes { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public List<BrigadeInspectionRemark> Remarks { get; set; } = [];
}
