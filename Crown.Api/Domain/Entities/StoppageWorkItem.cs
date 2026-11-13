namespace Crown.Domain.Entities;

/// <summary>Справочник выполняемых работ (выбор при остановке и топ проблемных работ).</summary>
public sealed class StoppageWorkItem
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
