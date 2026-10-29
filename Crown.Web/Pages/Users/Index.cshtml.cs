using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages.Users;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class UserListModel(CrownApiClient api) : PageModel
{
    public List<UserListItem> Items { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (items, hasMore, error, challenge) = await LoadAsync(0);
        if (challenge)
        {
            return Challenge();
        }

        Items = items;
        HasMore = hasMore;
        Error = error;
        return Page();
    }

    public async Task<IActionResult> OnGetMoreAsync(int skip)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        var (items, hasMore, _, challenge) = await LoadAsync(skip);
        if (challenge)
        {
            return Challenge();
        }

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        return Partial("_UserRows", items);
    }

    public async Task<IActionResult> OnGetMoreCardsAsync(int skip)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        var (items, hasMore, _, challenge) = await LoadAsync(skip);
        if (challenge)
        {
            return Challenge();
        }

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        return Partial("_UserCards", items);
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id, string? userName, string? confirmation)
    {
        // admin нельзя удалить — UI прячет кнопку, API тоже режет.
        if (string.Equals(userName, "admin", StringComparison.OrdinalIgnoreCase))
        {
            TempData["Warn"] = "Главного администратора удалить нельзя.";
            return Redirect("/users");
        }

        if (string.IsNullOrWhiteSpace(userName)
            || !string.Equals(confirmation?.Trim(), userName, StringComparison.Ordinal))
        {
            TempData["Warn"] = "Подтверждение не совпало с логином.";
            return Redirect("/users");
        }

        var result = await api.DeleteAsync($"api/users/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok)
        {
            TempData["Ok"] = $"Пользователь «{userName}» удалён.";
        }
        else
        {
            TempData["Warn"] = result.Error;
        }

        return Redirect("/users");
    }

    private async Task<(List<UserListItem> Items, bool HasMore, string? Error, bool Challenge)> LoadAsync(int skip)
    {
        var url = UiHelpers.BuildUrl(
            "api/users",
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<UserListItem>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return ([], false, null, true);
        }

        if (!result.Ok || result.Data is null)
        {
            return ([], false, result.Error, false);
        }

        return (result.Data.Items, result.Data.HasMore, null, false);
    }
}
