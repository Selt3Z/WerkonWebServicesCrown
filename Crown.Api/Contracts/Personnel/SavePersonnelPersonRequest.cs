namespace Crown.Contracts.Personnel;

public sealed class SavePersonnelPersonRequest
{
    /// <summary>
    /// Владелец списка. Для подразделения игнорируется (всегда сам).
    /// Для админа/диспетчера обязательно — персонал выбранного подразделения.
    /// </summary>
    public Guid? OwnerUserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    public string? Position { get; set; }

    public bool IsActive { get; set; } = true;
}
