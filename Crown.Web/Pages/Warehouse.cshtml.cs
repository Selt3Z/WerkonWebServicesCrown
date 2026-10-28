using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class WarehouseModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool InStock { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Shortage { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Archived { get; set; }

    [BindProperty(SupportsGet = true)]
    public int P { get; set; } = 1;

    public List<WarehousePart> Items { get; private set; } = [];

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
            "api/warehouse-parts",
            ("search", Q?.Trim()),
            ("category", Category),
            ("inStockOnly", InStock ? "true" : null),
            ("shortageOnly", Shortage ? "true" : null),
            ("includeArchived", Archived ? "true" : null),
            ("skip", ((P - 1) * UiHelpers.PageSize).ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<WarehousePart>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Status == 404)
        {
            // API отдаёт 404 на все складские методы, пока WarehouseEnabled выключен.
            Error = "Складской учёт отключён. Включите его в разделе «Настройки».";
            return Page();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            return Page();
        }

        Items = result.Data.Items;
        HasMore = result.Data.HasMore;

        var categories = await api.GetAsync<List<string>>("api/warehouse-parts/categories", HttpContext.RequestAborted);
        Categories = categories.Data ?? [];
        return Page();
    }

    public string PageUrl(int page) =>
        UiHelpers.BuildUrl(
            "/warehouse",
            ("q", Q),
            ("category", Category),
            ("inStock", InStock ? "true" : null),
            ("shortage", Shortage ? "true" : null),
            ("archived", Archived ? "true" : null),
            ("p", page > 1 ? page.ToString() : null));
}
