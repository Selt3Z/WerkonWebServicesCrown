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

    public string? Error { get; private set; }

    public sealed class InputModel
    {
        public string? UserName { get; set; }
        public string? DisplayName { get; set; }
        public string? Role { get; set; }
        public bool IsActive { get; set; }
        public string? Password { get; set; }
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

        Input = new InputModel
        {
            UserName = result.Data.UserName,
            DisplayName = result.Data.DisplayName,
            Role = result.Data.Role,
            IsActive = result.Data.IsActive
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id)
    {
        UserId = id;

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

        var body = new SaveUserRequest
        {
            UserName = Input.UserName.Trim(),
            DisplayName = Input.DisplayName.Trim(),
            Role = Input.Role,
            IsActive = Input.IsActive,
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
}
