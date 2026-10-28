namespace Crown.Contracts.Auth;

public sealed class UserListItemResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public DateTime? LastActivityAtUtc { get; set; }
}
