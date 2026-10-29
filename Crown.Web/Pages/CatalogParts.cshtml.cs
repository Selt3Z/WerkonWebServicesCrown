using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class CatalogPartsModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Category { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? State { get; set; }

    /// <summary>Для staff — чей каталог смотрим. Инициатор игнорирует (всегда свой).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? OwnerUserId { get; set; }

    public List<CatalogPart> Items { get; private set; } = [];

    public List<string> Categories { get; private set; } = [];

    public List<RequestAssignee> Owners { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public bool IsStaff =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    public bool CanEdit => !IsStaff || OwnerUserId is Guid;

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsStaff)
        {
            await LoadOwnersAsync();
            if (OwnerUserId is null && Owners.Count > 0)
            {
                // Не автовыбираем — staff явно выбирает, как в сводке.
            }
        }
        else
        {
            OwnerUserId = null; // API возьмёт текущего
        }

        var (items, hasMore, error, challenge) = await LoadAsync(0);
        if (challenge)
        {
            return Challenge();
        }

        Items = items;
        HasMore = hasMore;
        Error = error;

        var catUrl = UiHelpers.BuildUrl(
            "api/catalog-parts/categories",
            ("ownerUserId", EffectiveOwnerQuery()));
        var categories = await api.GetAsync<List<string>>(catUrl, HttpContext.RequestAborted);
        Categories = categories.Data ?? [];
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
        return Partial("_CatalogPartRows", items);
    }

    private string? EffectiveOwnerQuery() =>
        IsStaff ? OwnerUserId?.ToString() : null;

    private async Task LoadOwnersAsync()
    {
        var result = await api.GetAsync<List<RequestAssignee>>("api/requests/assignees", HttpContext.RequestAborted);
        if (result.Ok && result.Data is not null)
        {
            Owners = result.Data;
        }
    }

    private async Task<(List<CatalogPart> Items, bool HasMore, string? Error, bool Challenge)> LoadAsync(int skip)
    {
        if (IsStaff && OwnerUserId is null)
        {
            return ([], false, null, false);
        }

        var url = UiHelpers.BuildUrl(
            "api/catalog-parts",
            ("ownerUserId", EffectiveOwnerQuery()),
            ("search", Q?.Trim()),
            ("category", Category),
            ("activeFilter", State),
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<CatalogPart>>(url, HttpContext.RequestAborted);
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
