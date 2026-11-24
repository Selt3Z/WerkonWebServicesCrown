namespace Crown.Contracts.Auth;

public sealed class SaveUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string? Password { get; set; }
    /// <summary>Текущий пароль — обязателен при смене своего пароля (self).</summary>
    public string? CurrentPassword { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Номер бригады — обязателен для роли Brigade (макс. 50).</summary>
    public string? BrigadeNumber { get; set; }
    /// <summary>Цех бригады — обязателен для роли Brigade (макс. 100).</summary>
    public string? BrigadeWorkshop { get; set; }
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
