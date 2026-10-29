namespace Crown.Domain.Entities;

/// <summary>
/// Позиция каталога услуг подразделения.
/// OwnerUserId — пользователь-инициатор, чей это каталог (у админа/диспетчера своего нет).
/// DefaultUnitPrice оставлен в модели, в UI не показываем.
/// </summary>
public sealed class CatalogPart
{
    public Guid Id { get; set; }

    /// <summary>Владелец каталога (роль Initiator).</summary>
    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Category { get; set; }

    public decimal DefaultUnitPrice { get; set; }

    public string DefaultUnit { get; set; } = "шт";

    public string? Description { get; set; }

    /// <summary>Опциональный SLA в часах — подсказка срока при выборе из каталога.</summary>
    public int? SlaHours { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
