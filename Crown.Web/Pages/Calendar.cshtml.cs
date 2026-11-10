using System.Globalization;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>Календарь: та же очередь, что «Сегодня», с выбором даты.</summary>
[Authorize]
public sealed class CalendarModel(CrownApiClient api, AppTime time) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Date { get; set; }

    public DateOnly SelectedDate { get; private set; }

    public bool IsToday { get; private set; }

    public List<ServiceRequestListItem> Incoming { get; private set; } = [];

    public List<ServiceRequestListItem> Outgoing { get; private set; } = [];

    public string? Error { get; private set; }

    public string DateInputValue => SelectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string PrevDateHref => $"/calendar?date={SelectedDate.AddDays(-1):yyyy-MM-dd}";

    public string NextDateHref => $"/calendar?date={SelectedDate.AddDays(1):yyyy-MM-dd}";

    public async Task<IActionResult> OnGetAsync()
    {
        if (User.IsInRole(AppRoles.Administrator))
        {
            return Redirect("/");
        }

        var today = time.GetToday();
        if (string.IsNullOrWhiteSpace(Date) || !DateOnly.TryParse(Date, CultureInfo.InvariantCulture, out var parsed))
        {
            SelectedDate = today;
        }
        else
        {
            SelectedDate = parsed;
        }

        IsToday = SelectedDate == today;

        // Канонический URL без ?date= для сегодня.
        if (IsToday && !string.IsNullOrWhiteSpace(Date))
        {
            return Redirect("/calendar");
        }

        var url = IsToday
            ? "api/requests/today"
            : UiHelpers.BuildUrl("api/requests/today", ("date", SelectedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        var result = await api.GetAsync<TodayRequestsResponse>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить очередь на день.";
            return Page();
        }

        SelectedDate = result.Data.Date;
        IsToday = SelectedDate == today;
        Incoming = result.Data.Incoming;
        Outgoing = result.Data.Outgoing;
        return Page();
    }
}
