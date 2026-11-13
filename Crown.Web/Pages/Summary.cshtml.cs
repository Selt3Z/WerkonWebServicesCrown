using System.Security.Claims;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class SummaryModel(CrownApiClient api, AppTime time) : PageModel
{
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanViewSummaryClaim)?.Value == "1";

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "today";

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? UserId { get; set; }

    public SummaryResponse? Summary { get; private set; }

    public List<RequestAssignee> AvailableUsers { get; private set; } = [];

    public string? Error { get; private set; }

    public bool CanPickUser =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    /// <summary>Staff без выбранного подразделения — сводку не грузим.</summary>
    public bool NeedsUserPick => CanPickUser && (UserId is null || UserId == Guid.Empty);

    /// <summary>От года самой старой заявки до «сегодня» в AppTimeZone.</summary>
    public IReadOnlyList<int> AvailableYears { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        Period = SummaryReportPeriods.Normalize(Period);
        var today = time.GetToday();
        Year ??= today.Year;
        Month ??= today.Month;

        AvailableYears = await LoadAvailableYearsAsync(today.Year);

        // Обычный пользователь всегда смотрит себя — query userId игнорируем.
        if (!CanPickUser)
        {
            UserId = null;
        }

        if (NeedsUserPick)
        {
            var assignees = await api.GetAsync<List<RequestAssignee>>("api/requests/assignees", HttpContext.RequestAborted);
            if (assignees.IsUnauthorized)
            {
                return Challenge();
            }

            AvailableUsers = assignees.Data ?? [];
            return Page();
        }

        var url = UiHelpers.BuildUrl(
            "api/summary",
            ("period", Period),
            ("year", Year?.ToString()),
            ("month", Month?.ToString()),
            ("userId", CanPickUser ? UserId?.ToString() : null));

        var result = await api.GetAsync<SummaryResponse>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить сводку.";
            return Page();
        }

        Summary = result.Data;
        if (CanPickUser)
        {
            UserId = Summary.UserId;
            AvailableUsers = Summary.AvailableUsers?
                .Select(u => new RequestAssignee
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    DisplayName = u.DisplayName
                })
                .ToList() ?? [];
        }

        return Page();
    }

    /// <summary>«Месяц» без year/month — всегда текущий месяц; year/month только в пикерах формы.</summary>
    public string PeriodUrl(string period) =>
        UiHelpers.BuildUrl(
            "/summary",
            ("period", period),
            ("year", period == "year" ? Year?.ToString() : null),
            ("userId", CanPickUser ? UserId?.ToString() : null));

    public string SelfId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private async Task<IReadOnlyList<int>> LoadAvailableYearsAsync(int todayYear)
    {
        var earliest = todayYear;
        var result = await api.GetAsync<int>("api/summary/earliest-year", HttpContext.RequestAborted);
        if (result.Ok)
        {
            earliest = Math.Min(result.Data, todayYear);
        }

        if (earliest > todayYear)
        {
            earliest = todayYear;
        }

        return Enumerable.Range(earliest, todayYear - earliest + 1).Reverse().ToList();
    }
}

internal static class SummaryReportPeriods
{
    public static string Normalize(string? period) =>
        (period ?? "today").Trim().ToLowerInvariant() switch
        {
            "week" => "week",
            "month" => "month",
            "year" => "year",
            _ => "today"
        };
}
