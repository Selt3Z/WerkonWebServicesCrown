using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class SettingsModel(CrownApiClient api, AppTime appTime) : PageModel
{
    [BindProperty]
    public SettingsResponse Input { get; set; } = new();

    public IReadOnlyList<TimeZoneInfo> AvailableTimeZones { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await api.GetAsync<SettingsResponse>("api/settings", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            AvailableTimeZones = TimeZoneDisplayHelper.GetOrderedTimeZones();
            return Page();
        }

        Input = result.Data;
        // Подтягиваем пояс из API в Web (как SetTimeZoneId после сохранения в Ratchet).
        appTime.SetTimeZoneId(Input.TimeZoneId);
        AvailableTimeZones = TimeZoneDisplayHelper.GetOrderedTimeZones(Input.TimeZoneId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        AvailableTimeZones = TimeZoneDisplayHelper.GetOrderedTimeZones(Input.TimeZoneId);

        // Валюту не трогаем: в заявках денег нет, хост всегда RUB.
        var result = await api.PutAsync<NoContent>(
            "api/settings",
            new UpdateSettingsRequest
            {
                HideArchivedByDefault = Input.HideArchivedByDefault,
                DispatcherCanHardDelete = Input.DispatcherCanHardDelete,
                WarehouseEnabled = Input.WarehouseEnabled,
                AuditRetentionDays = Input.AuditRetentionDays,
                ListPageSize = Input.ListPageSize
            },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Ok"] = "Настройки сохранены.";
        return Redirect("/settings");
    }

    public async Task<IActionResult> OnPostTimeZoneAsync(string? timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            TempData["Warn"] = "Выберите часовой пояс.";
            return Redirect("/settings");
        }

        var result = await api.PutAsync<NoContent>(
            "api/settings/timezone",
            new UpdateTimeZoneRequest { TimeZoneId = timeZoneId.Trim() },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok)
        {
            appTime.SetTimeZoneId(timeZoneId.Trim());
            TempData["Ok"] = "Часовой пояс сохранён.";
        }
        else
        {
            TempData["Warn"] = result.Error;
        }

        return Redirect("/settings");
    }
}
