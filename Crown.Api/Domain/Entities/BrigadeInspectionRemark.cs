namespace Crown.Domain.Entities;

/// <summary>Нарушение (пункт акта) в проверке бригады. Зеркало <see cref="StoppageRemark"/> без «устранено».</summary>
public sealed class BrigadeInspectionRemark
{
    public Guid Id { get; set; }

    public Guid InspectionId { get; set; }

    public BrigadeInspection Inspection { get; set; } = null!;

    /// <summary>Ссылка на справочник пунктов акта (null = вручную или пункт удалён из справочника).</summary>
    public Guid? ActItemId { get; set; }

    /// <summary>Текст пункта (снимок из справочника или ручной ввод).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Комментарий к пункту из справочника.</summary>
    public string? Comment { get; set; }

    /// <summary>true = внештатный пункт «Вручную».</summary>
    public bool IsManual { get; set; }

    public int SortOrder { get; set; }
}
