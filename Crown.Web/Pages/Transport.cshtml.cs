using System.Globalization;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class TransportModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? ParkingId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public TransportBoardResponse Board { get; private set; } = new();

    public string? Error { get; private set; }

    public bool CanManageFleet =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    public async Task<IActionResult> OnGetAsync()
    {
        var loaded = await LoadBoardAsync();
        if (!loaded)
        {
            return Challenge();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostIssueAsync(
        Guid vehicleId,
        Guid borrowerUserId,
        string mode,
        string? plannedStartLocal,
        string plannedEndLocal,
        string? note)
    {
        if (!TryParseLocal(plannedEndLocal, out var end))
        {
            TempData["Warn"] = "Укажите корректное время окончания.";
            return RedirectToPage(new { ParkingId, Q });
        }

        DateTimeOffset? start = null;
        if (string.Equals(mode, "book", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseLocal(plannedStartLocal, out var s))
            {
                TempData["Warn"] = "Укажите время начала брони.";
                return RedirectToPage(new { ParkingId, Q });
            }

            start = s;
        }

        var body = new CreateVehicleTripRequest
        {
            VehicleId = vehicleId,
            BorrowerUserId = borrowerUserId,
            PlannedStartAt = start,
            PlannedEndAt = end,
            Note = note
        };

        var result = await api.PostAsync<VehicleTripItem>("api/transport/trips", body, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error ?? "Не удалось оформить.";
        }
        else
        {
            TempData["Ok"] = start is null ? "Машина выдана." : "Бронь создана.";
        }

        return RedirectToPage(new { ParkingId, Q });
    }

    public async Task<IActionResult> OnPostStartAsync(Guid tripId, int? odometerStart, decimal? fuelStart)
    {
        var result = await api.PostAsync<VehicleTripItem>(
            $"api/transport/trips/{tripId}/start",
            new StartVehicleTripRequest { OdometerStart = odometerStart, FuelStart = fuelStart },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        TempData[result.Ok ? "Ok" : "Warn"] = result.Ok ? "Поездка начата." : result.Error;
        return RedirectToPage(new { ParkingId, Q });
    }

    public async Task<IActionResult> OnPostReturnAsync(
        Guid tripId,
        Guid? returnParkingId,
        int? odometerEnd,
        string? note)
    {
        var result = await api.PostAsync<VehicleTripItem>(
            $"api/transport/trips/{tripId}/return",
            new ReturnVehicleTripRequest
            {
                ReturnParkingId = returnParkingId,
                OdometerEnd = odometerEnd,
                Note = note
            },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok && result.Data is not null)
        {
            var late = result.Data.LateMinutes is > 0
                ? $", опоздание {FormatLate(result.Data.LateMinutes.Value)}"
                : "";
            TempData["Ok"] = $"Сдано · {result.Data.UsedHours:0.##} ч{late}.";
        }
        else
        {
            TempData["Warn"] = result.Error ?? "Не удалось вернуть.";
        }

        return RedirectToPage(new { ParkingId, Q });
    }

    public async Task<IActionResult> OnPostCancelAsync(Guid tripId)
    {
        var result = await api.PostAsync<VehicleTripItem>(
            $"api/transport/trips/{tripId}/cancel",
            new { },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        TempData[result.Ok ? "Ok" : "Warn"] = result.Ok ? "Бронь отменена." : result.Error;
        return RedirectToPage(new { ParkingId, Q });
    }

    public async Task<IActionResult> OnPostAddParkingAsync(string name, string? address)
    {
        if (!CanManageFleet)
        {
            return Forbid();
        }

        var result = await api.PostAsync<ParkingLotItem>(
            "api/transport/parkings",
            new SaveParkingLotRequest { Name = name, Address = address, IsActive = true },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        TempData[result.Ok ? "Ok" : "Warn"] = result.Ok ? "Стоянка добавлена." : result.Error;
        return RedirectToPage(new { ParkingId, Q });
    }

    public async Task<IActionResult> OnPostAddVehicleAsync(
        string plateNumber,
        string name,
        string? vehicleType,
        string? vin,
        bool isInRepair,
        Guid? currentParkingId)
    {
        if (!CanManageFleet)
        {
            return Forbid();
        }

        var result = await api.PostAsync<TransportVehicleItem>(
            "api/transport/vehicles",
            new SaveVehicleRequest
            {
                PlateNumber = plateNumber,
                Name = name,
                VehicleType = vehicleType,
                Vin = vin,
                IsInRepair = isInRepair,
                CurrentParkingId = currentParkingId,
                IsActive = true
            },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        TempData[result.Ok ? "Ok" : "Warn"] = result.Ok ? "Машина добавлена." : result.Error;
        return RedirectToPage(new { ParkingId, Q });
    }

    /// <returns>false = Challenge (401).</returns>
    private async Task<bool> LoadBoardAsync()
    {
        var url = UiHelpers.BuildUrl(
            "api/transport/board",
            ("parkingId", ParkingId?.ToString()),
            ("search", Q?.Trim()));
        var result = await api.GetAsync<TransportBoardResponse>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return false;
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить транспорт.";
            return true;
        }

        Board = result.Data;
        return true;
    }

    private static bool TryParseLocal(string? raw, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // datetime-local → локаль браузера без offset; считаем как Unspecified → offset текущего TZ сервера UI.
        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local)
            || DateTime.TryParse(raw, out local))
        {
            value = new DateTimeOffset(local);
            return true;
        }

        return false;
    }

    private static string FormatLate(int minutes)
    {
        if (minutes < 60)
        {
            return $"{minutes} мин";
        }

        var h = minutes / 60;
        var m = minutes % 60;
        return m == 0 ? $"{h} ч" : $"{h} ч {m} мин";
    }
}
