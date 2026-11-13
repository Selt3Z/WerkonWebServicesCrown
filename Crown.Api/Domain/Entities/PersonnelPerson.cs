namespace Crown.Domain.Entities;

/// <summary>
/// Сотрудник подразделения (не логин). OwnerUserId — Department, чей это список.
/// У админа/диспетчера своего списка нет — смотрят чужие, как каталог.
/// </summary>
public sealed class PersonnelPerson
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? Position { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }
}
