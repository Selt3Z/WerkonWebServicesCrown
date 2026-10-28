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

    public List<WarehousePart> Items { get; private set; } = [];

    public List<string> Categories { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (items, hasMore, error, challenge, disabled) = await LoadAsync(0);
        if (challenge)
        {
            return Challenge();
        }

        Items = items;
        HasMore = hasMore;
        Error = error;
        if (!disabled)
        {
            var categories = await api.GetAsync<List<string>>("api/warehouse-parts/categories", HttpContext.RequestAborted);
            Categories = categories.Data ?? [];
        }

        return Page();
    }

    public async Task<IActionResult> OnGetMoreAsync(int skip)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        var (items, hasMore, _, challenge, _) = await LoadAsync(skip);
        if (challenge)
        {
            return Challenge();
        }

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        return Partial("_WarehouseRows", items);
    }

    private async Task<(List<WarehousePart> Items, bool HasMore, string? Error, bool Challenge, bool Disabled)> LoadAsync(int skip)
    {
        var url = UiHelpers.BuildUrl(
            "api/warehouse-parts",
            ("search", Q?.Trim()),
            ("category", Category),
            ("inStockOnly", InStock ? "true" : null),
            ("shortageOnly", Shortage ? "true" : null),
            ("includeArchived", Archived ? "true" : null),
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<WarehousePart>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return ([], false, null, true, false);
        }

        if (result.Status == 404)
        {
            return ([], false, "Складской учёт отключён. Включите его в разделе «Настройки».", false, true);
        }

        if (!result.Ok || result.Data is null)
        {
            return ([], false, result.Error, false, false);
        }

        return (result.Data.Items, result.Data.HasMore, null, false, false);
    }
}
