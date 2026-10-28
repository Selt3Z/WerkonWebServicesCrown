using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class CatalogPartsModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    /// <summary>"", "active" или "inactive" — как activeFilter в API.</summary>
    [BindProperty(SupportsGet = true)]
    public string? State { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<CatalogPart> Items { get; private set; } = [];

    public List<string> Categories { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (P < 1)
        {
            P = 1;
        }

        var url = UiHelpers.BuildUrl(
            "api/catalog-parts",
            ("search", Q?.Trim()),
            ("category", Category),
            ("activeFilter", State),
            ("skip", ((P - 1) * UiHelpers.PageSize).ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<CatalogPart>>(url, HttpContext.RequestAborted);
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

        var categories = await api.GetAsync<List<string>>("api/catalog-parts/categories", HttpContext.RequestAborted);
        Categories = categories.Data ?? [];
        return Page();
    }

    public string PageUrl(int page) =>
        UiHelpers.BuildUrl(
            "/catalog-parts",
            ("q", Q),
            ("category", Category),
            ("state", State),
            ("p", page > 1 ? page.ToString() : null));
}
