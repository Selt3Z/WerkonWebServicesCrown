using Crown.Domain.Entities;

namespace Crown.Contracts.BrigadeRating;

public sealed class BrigadeInspectionRemarkResponse
{
    public Guid Id { get; set; }
    public Guid? ActItemId { get; set; }
    public string Text { get; set; } = string.Empty;
    public string? Comment { get; set; }
    public bool IsManual { get; set; }
    public int SortOrder { get; set; }
}

public class BrigadeInspectionListItemResponse
{
    public Guid Id { get; set; }
    public BrigadeInspectionKind Kind { get; set; }
    public int SequenceNo { get; set; }
    public DateOnly ReportDate { get; set; }
    public string Workshop { get; set; } = string.Empty;
    public Guid BrigadeUserId { get; set; }
    public string BrigadeNumber { get; set; } = string.Empty;
    public Guid? MasterPersonId { get; set; }
    public string MasterName { get; set; } = string.Empty;
    public string? Well { get; set; }
    public Guid? CheckerPersonId { get; set; }
    public string CheckerName { get; set; } = string.Empty;
    public string? CheckerPosition { get; set; }
    public DateOnly? FixDate { get; set; }
    public bool FixDateIsManual { get; set; }
    public string? FixDateManualText { get; set; }

    // Supervising
    public BrigadeInspectionOutcome? Outcome { get; set; }
    public bool StopPoint { get; set; }
    public bool HasExplanation { get; set; }
    public Guid? DepartmentUserId { get; set; }
    public string? DepartmentName { get; set; }

    // Pdk
    public string? FixResponsibleText { get; set; }

    // Pbiot
    public string? FixReportText { get; set; }
    public Guid? FixResponsiblePersonId { get; set; }
    public string? FixResponsibleName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }

    public int RemarkCount { get; set; }

    /// <summary>Тексты нарушений по порядку (с комментарием справочника в скобках).</summary>
    public List<string> RemarkTexts { get; set; } = [];
}

public sealed class BrigadeInspectionDetailResponse : BrigadeInspectionListItemResponse
{
    public List<BrigadeInspectionRemarkResponse> Remarks { get; set; } = [];
}

public sealed class SaveInspectionRemarkRequest
{
    public Guid? ActItemId { get; set; }

    /// <summary>Ручной текст (если IsManual / без ActItemId).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Комментарий к пункту из справочника.</summary>
    public string? Comment { get; set; }

    public bool IsManual { get; set; }
}

public sealed class SaveBrigadeInspectionRequest
{
    /// <summary>Только при создании; в PUT вид менять нельзя (игнорируется).</summary>
    public BrigadeInspectionKind Kind { get; set; }

    public DateOnly ReportDate { get; set; }
    public Guid BrigadeUserId { get; set; }

    /// <summary>Мастер из персонала бригады; MasterName берётся из FullName.</summary>
    public Guid? MasterPersonId { get; set; }

    /// <summary>Мастер вручную (если не выбран из персонала и не создаётся новый).</summary>
    public string? MasterName { get; set; }

    /// <summary>Создать нового мастера в персонале бригады (если задан NewMasterFullName).</summary>
    public string? NewMasterFullName { get; set; }
    public string? NewMasterPosition { get; set; }

    public string? Well { get; set; }

    /// <summary>Подразделение проверяющего; владелец персонала проверяющего.</summary>
    public Guid? DepartmentUserId { get; set; }

    public Guid? CheckerPersonId { get; set; }
    public string? CheckerName { get; set; }
    public string? CheckerPosition { get; set; }

    /// <summary>Создать нового проверяющего в персонале подразделения (нужен DepartmentUserId).</summary>
    public string? NewCheckerFullName { get; set; }
    public string? NewCheckerPosition { get; set; }

    public DateOnly? FixDate { get; set; }
    public bool FixDateIsManual { get; set; }
    public string? FixDateManualText { get; set; }

    // Supervising
    public BrigadeInspectionOutcome? Outcome { get; set; }
    public bool StopPoint { get; set; }
    public bool HasExplanation { get; set; }

    // Pdk
    public string? FixResponsibleText { get; set; }

    // Pbiot
    public string? FixReportText { get; set; }
    public Guid? FixResponsiblePersonId { get; set; }
    public string? FixResponsibleName { get; set; }

    public List<SaveInspectionRemarkRequest> Remarks { get; set; } = [];
}

/// <summary>Сотрудник бригады цеха — кандидат в «ответственные за устранение» (ПБиОТ).</summary>
public sealed class WorkshopPersonnelResponse
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }
    public Guid OwnerUserId { get; set; }
    public string BrigadeName { get; set; } = string.Empty;
}
