using System.Security.Claims;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages.Users;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class UserEditModel(CrownApiClient api) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new() { Role = AppRoles.Initiator, IsActive = true };

    public Guid? UserId { get; private set; }

    public bool IsNew => UserId is null;

    /// <summary>При смене собственного пароля API требует текущий.</summary>
    public bool IsSelf =>
        UserId is not null && User.FindFirstValue(ClaimTypes.NameIdentifier) == UserId.ToString();

    /// <summary>Seed-аккаунт admin: логин и роль только через конфиг/БД.</summary>
    public bool IsProtectedAdmin { get; private set; }

    public DateTime? LastActivityAtUtc { get; private set; }

    public string? Error { get; private set; }

    public sealed class InputModel
    {
        public string? UserName { get; set; }
        public string? DisplayName { get; set; }
        public string? Role { get; set; }
        public bool IsActive { get; set; }
        public string? Password { get; set; }
        public string? ConfirmPassword { get; set; }
        public string? CurrentPassword { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        UserId = id;
        if (id is null)
        {
            return Page();
        }

        var result = await api.GetAsync<UserListItem>($"api/users/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Status == 404)
        {
            return NotFound();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            return Page();
        }

        ApplyUser(result.Data);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id)
    {
        UserId = id;
        IsProtectedAdmin = !IsNew
            && string.Equals(Input.UserName, "admin", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(Input.UserName)
            || string.IsNullOrWhiteSpace(Input.DisplayName)
            || string.IsNullOrWhiteSpace(Input.Role))
        {
            Error = "Заполните логин, имя и роль.";
            return Page();
        }

        if (IsNew && string.IsNullOrWhiteSpace(Input.Password))
        {
            Error = "Для нового пользователя нужен пароль.";
            return Page();
        }

        if (!string.IsNullOrWhiteSpace(Input.Password) || IsNew)
        {
            if (string.IsNullOrWhiteSpace(Input.ConfirmPassword))
            {
                Error = "Подтвердите пароль.";
                return Page();
            }

            if (!string.Equals(Input.Password, Input.ConfirmPassword, StringComparison.Ordinal))
            {
                Error = "Пароль и подтверждение не совпадают.";
                return Page();
            }

            if (IsSelf && string.IsNullOrWhiteSpace(Input.CurrentPassword))
            {
                Error = "Укажите текущий пароль.";
                return Page();
            }
        }

        var body = new SaveUserRequest
        {
            UserName = Input.UserName.Trim(),
            DisplayName = Input.DisplayName.Trim(),
            Role = Input.Role,
            // Свой профиль / seed-admin: чекбокс скрыт, IsActive уходит hidden-полем с исходным значением.
            IsActive = IsProtectedAdmin || IsSelf || Input.IsActive,
            Password = string.IsNullOrWhiteSpace(Input.Password) ? null : Input.Password,
            CurrentPassword = string.IsNullOrWhiteSpace(Input.CurrentPassword) ? null : Input.CurrentPassword
        };

        var result = IsNew
            ? await api.PostAsync<UserListItem>("api/users", body, HttpContext.RequestAborted)
            : await api.PutAsync<UserListItem>($"api/users/{id}", body, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Ok"] = "Пользователь сохранён.";
        return Redirect("/users");
    }

    private void ApplyUser(UserListItem user)
    {
        IsProtectedAdmin = string.Equals(user.UserName, "admin", StringComparison.OrdinalIgnoreCase);
        LastActivityAtUtc = user.LastActivityAtUtc;
        Input = new InputModel
        {
            UserName = user.UserName,
            DisplayName = user.DisplayName,
            Role = user.Role,
            IsActive = user.IsActive
        };
    }
}
