using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class EquipmentModel(CrownApiClient api) : PageModel
{
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanManageEquipmentClaim)?.Value == "1";

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool FreeOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Archived { get; set; }

    public List<EquipmentAsset> Items { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

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
        if (!CanAccess)
        {
            return Forbid();
        }

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
        return Partial("_EquipmentRows", items);
    }

    private async Task<(List<EquipmentAsset> Items, bool HasMore, string? Error, bool Challenge)> LoadAsync(int skip)
    {
        var url = UiHelpers.BuildUrl(
            "api/equipment",
            ("search", Q?.Trim()),
            ("freeOnly", FreeOnly ? "true" : null),
            ("includeArchived", Archived ? "true" : null),
            ("skip", skip.ToString()),
            ("take", "40"));

        var result = await api.GetAsync<PagedResponse<EquipmentAsset>>(url, HttpContext.RequestAborted);
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
