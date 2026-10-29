using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = $"{AppRoles.Administrator},{AppRoles.Dispatcher}")]
public sealed class TransportVehicleEditModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public SaveVehicleRequest Edit { get; set; } = new();

    public List<ParkingLotItem> Parkings { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await LoadAsync())
        {
            return Challenge();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var result = await api.PutAsync<TransportVehicleItem>(
            $"api/transport/vehicles/{Id}",
            Edit,
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error ?? "Не удалось сохранить.";
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Карточка машины сохранена.";
        return Redirect($"/transport/vehicles/{Id}");
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

        var v = detail.Data.Vehicle;
        Edit = new SaveVehicleRequest
        {
            PlateNumber = v.PlateNumber,
            Name = v.Name,
            VehicleType = v.VehicleType,
            Vin = v.Vin,
            IsInRepair = v.IsInRepair,
            CurrentParkingId = v.CurrentParkingId,
            IsActive = true
        };

        var parkings = await api.GetAsync<List<ParkingLotItem>>("api/transport/parkings", HttpContext.RequestAborted);
        Parkings = parkings.Data?.Where(x => x.IsActive).ToList() ?? [];
        return true;
    }
}
