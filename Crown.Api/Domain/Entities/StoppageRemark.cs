namespace Crown.Domain.Entities;

/// <summary>Пункт акта предписания / замечание по остановке.</summary>
public sealed class StoppageRemark
{
    public Guid Id { get; set; }

    public Guid BrigadeStoppageId { get; set; }

    public BrigadeStoppage Stoppage { get; set; } = null!;

    /// <summary>Ссылка на справочник (null = вручную или старая запись).</summary>
    public Guid? ActItemId { get; set; }

    /// <summary>Текст пункта (снимок из справочника или ручной ввод).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Комментарий к пункту из справочника — в Excel в скобках.</summary>
    public string Comment { get; set; } = string.Empty;

    /// <summary>true = внештатный пункт «Вручную» (в Excel без скобок).</summary>
    public bool IsManual { get; set; }

    /// <summary>Устранено (влияет на рейтинг).</summary>
    public bool IsResolved { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public int SortOrder { get; set; }
}
