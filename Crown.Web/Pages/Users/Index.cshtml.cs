using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages.Users;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class UserListModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<UserListItem> Items { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (P < 1)
        {
            P = 1;
        }

        var url = UiHelpers.BuildUrl(
            "api/users",
            ("skip", ((P - 1) * UiHelpers.PageSize).ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<UserListItem>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            return Page();
        }

        Items = result.Data.Items;
        HasMore = result.Data.HasMore;
        return Page();
    }

    public string PageUrl(int page) =>
        UiHelpers.BuildUrl("/users", ("p", page > 1 ? page.ToString() : null));
}
