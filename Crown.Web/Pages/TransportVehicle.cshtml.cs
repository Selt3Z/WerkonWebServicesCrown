using System.Globalization;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class TransportVehicleModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    public VehicleDetailResponse? Detail { get; private set; }

    public string? Error { get; private set; }

    public bool CanManageFleet =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return Challenge();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!CanManageFleet)
        {
            return Forbid();
        }

        var result = await api.DeleteAsync($"api/transport/vehicles/{Id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error ?? "Не удалось удалить.";
            return RedirectToPage(new { Id });
        }

        TempData["Ok"] = "Машина удалена.";
        return RedirectToPage("/Transport");
    }

    public async Task<IActionResult> OnPostAddRefuelAsync(
        string fuelType,
        decimal liters,
        decimal pricePerLiter,
        string? refueledAtLocal,
        string? note)
    {
        DateTimeOffset? at = null;
        if (!string.IsNullOrWhiteSpace(refueledAtLocal)
            && (DateTime.TryParse(refueledAtLocal, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)
                || DateTime.TryParse(refueledAtLocal, out local)))
        {
            at = new DateTimeOffset(local);
        }

        var result = await api.PostAsync<VehicleFuelRefuelItem>(
            $"api/transport/vehicles/{Id}/refuels",
            new SaveVehicleFuelRefuelRequest
            {
                RefueledAt = at,
                FuelType = fuelType,
                Liters = liters,
                PricePerLiter = pricePerLiter,
                Note = note
            },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        TempData[result.Ok ? "Ok" : "Warn"] = result.Ok ? "Заправка добавлена." : result.Error;
        return RedirectToPage(new { Id });
    }

    public async Task<IActionResult> OnPostDeleteRefuelAsync(Guid refuelId)
    {
        if (!CanManageFleet)
        {
            return Forbid();
        }

        var result = await api.DeleteAsync($"api/transport/vehicles/{Id}/refuels/{refuelId}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        TempData[result.Ok ? "Ok" : "Warn"] = result.Ok ? "Заправка удалена." : result.Error;
        return RedirectToPage(new { Id });
    }

    private async Task<bool> LoadAsync()
    {
        var detail = await api.GetAsync<VehicleDetailResponse>($"api/transport/vehicles/{Id}", HttpContext.RequestAborted);
        if (detail.IsUnauthorized)
        {
            return false;
        }

        if (!detail.Ok || detail.Data is null)
        {
            Error = detail.Error ?? "Машина не найдена.";
            return true;
        }

        Detail = detail.Data;
        return true;
    }
}
