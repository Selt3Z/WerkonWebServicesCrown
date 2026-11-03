using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class EquipmentDetailModel(CrownApiClient api) : PageModel
{
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanManageEquipmentClaim)?.Value == "1";

    public EquipmentAssetDetail? Item { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var result = await api.GetAsync<EquipmentAssetDetail>($"api/equipment/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не найдено.";
            return Page();
        }

        Item = result.Data;
        return Page();
    }

    public async Task<IActionResult> OnPostArchiveAsync(Guid id, bool archive)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var result = await api.PostAsync<EquipmentAsset>(
            $"api/equipment/{id}/archive?archive={archive.ToString().ToLowerInvariant()}",
            new { },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error;
        }
        else
        {
            TempData["Ok"] = archive ? "В архиве." : "Восстановлено.";
        }

        return Redirect($"/equipment/{id}");
    }
}
