namespace Crown.Contracts.Auth;

public sealed class SaveUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string? Password { get; set; }
    /// <summary>Текущий пароль — обязателен при смене своего пароля (self).</summary>
    public string? CurrentPassword { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
