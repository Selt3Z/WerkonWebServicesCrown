namespace Crown.Contracts.Personnel;

/// <summary>Подразделение или бригада — владелец списка персонала.</summary>
public sealed class PersonnelOwnerOptionResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class PersonnelPersonResponse
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public string OwnerDisplayName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string? Position { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
