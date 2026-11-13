namespace Crown.Contracts.Personnel;

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
