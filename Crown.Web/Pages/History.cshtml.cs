using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class HistoryModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EntityType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<AuditLogItem> Items { get; private set; } = [];

    public List<AuditLogUserOption> Users { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (P < 1)
        {
            P = 1;
        }

        var url = UiHelpers.BuildUrl(
            "api/audit-log",
            ("userId", UserId?.ToString()),
            ("entityType", EntityType?.Trim()),
            ("dateFrom", From),
            ("dateTo", To),
            ("skip", ((P - 1) * UiHelpers.PageSize).ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<AuditLogItem>>(url, HttpContext.RequestAborted);
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

        var users = await api.GetAsync<List<AuditLogUserOption>>("api/audit-log/users", HttpContext.RequestAborted);
        Users = users.Data ?? [];
        return Page();
    }

    public string PageUrl(int page) =>
        UiHelpers.BuildUrl(
            "/history",
            ("userId", UserId?.ToString()),
            ("entityType", EntityType),
            ("from", From),
            ("to", To),
            ("p", page > 1 ? page.ToString() : null));
}
