using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

public sealed class IndexModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<ServiceRequestListItem> Items { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (P < 1)
        {
            P = 1;
        }

        var url = UiHelpers.BuildUrl(
            "api/requests",
            ("search", Q?.Trim()),
            ("skip", ((P - 1) * UiHelpers.PageSize).ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<ServiceRequestListItem>>(url, HttpContext.RequestAborted);
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
        UiHelpers.BuildUrl("/", ("q", Q), ("p", page > 1 ? page.ToString() : null));
}
