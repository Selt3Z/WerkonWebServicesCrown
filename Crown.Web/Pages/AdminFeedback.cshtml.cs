using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class AdminFeedbackModel(CrownApiClient api, AppTime appTime) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public List<FeedbackListItem> Items { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var url = UiHelpers.BuildUrl("api/feedback", ("q", string.IsNullOrWhiteSpace(Q) ? null : Q.Trim()));
        var result = await api.GetAsync<List<FeedbackListItem>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить обращения.";
            return Page();
        }

        Items = result.Data;
        return Page();
    }

    public string FmtWhen(DateTimeOffset utc) =>
        appTime.Format(utc);
}
