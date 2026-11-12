namespace Crown.Domain.Entities;

/// <summary>Справочник пунктов по акту предписания (для выбора при остановке и топа).</summary>
public sealed class StoppageActItem
{
    public Guid Id { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
