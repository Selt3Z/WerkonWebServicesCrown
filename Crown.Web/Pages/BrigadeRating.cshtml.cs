using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class BrigadeRatingModel(CrownApiClient api, AppTime time) : PageModel
{
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanViewBrigadeRatingClaim)?.Value == "1"
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    /// <summary>Ввод остановок — staff или оператор рейтинга.</summary>
    public bool CanEnter =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    [BindProperty(SupportsGet = true)]
    public string Period { get; set; } = "month";

    [BindProperty(SupportsGet = true)]
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Month { get; set; }

    public BrigadeRatingDashboard? Dashboard { get; private set; }

    public string? Error { get; private set; }

    /// <summary>От года самой старой остановки до «сегодня» (AppTimeZone).</summary>
    public IReadOnlyList<int> AvailableYears { get; private set; } = [];

    /// <summary>
    /// Переключатель пресетов: «Месяц» всегда текущий (без year/month из предыдущего вида),
    /// иначе после «Год» уезжает январь (PeriodStart.Month == 1).
    /// </summary>
    public string PeriodUrl(string period) =>
        UiHelpers.BuildUrl(
            "/brigade-rating",
            ("period", period),
            ("year", period == "year" ? Year?.ToString() : null));

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var period = NormalizePeriod(Period);
        Period = period;
        var today = time.GetToday();

        var url = UiHelpers.BuildUrl(
            "api/brigade-rating/dashboard",
            ("period", period == "custom" ? null : period),
            ("from", period == "custom" ? From?.ToString("yyyy-MM-dd") : null),
            ("to", period == "custom" ? To?.ToString("yyyy-MM-dd") : null),
            ("year", period is "month" or "year" ? Year?.ToString() : null),
            ("month", period == "month" ? Month?.ToString() : null));

        // custom без дат — API возьмёт текущий месяц; иначе явный from/to.
        if (period == "custom" && From is null && To is null)
        {
            url = UiHelpers.BuildUrl("api/brigade-rating/dashboard", ("period", "month"));
            Period = "month";
        }

        var result = await api.GetAsync<BrigadeRatingDashboard>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить рейтинг бригад.";
            AvailableYears = [today.Year];
            return Page();
        }

        Dashboard = result.Data;
        Period = Dashboard.Period;
        From = Dashboard.PeriodStart;
        To = Dashboard.PeriodEnd;
        Year = Dashboard.PeriodStart.Year;
        Month = Dashboard.PeriodStart.Month;
        AvailableYears = BuildYears(Dashboard.EarliestYear, today.Year);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(
        string? period,
        DateOnly? from,
        DateOnly? to,
        int? year,
        int? month)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var key = NormalizePeriod(period);
        var today = time.GetToday();
        var url = key == "custom"
            ? UiHelpers.BuildUrl(
                "api/brigade-rating/export.xlsx",
                ("from", from?.ToString("yyyy-MM-dd")),
                ("to", to?.ToString("yyyy-MM-dd")))
            : UiHelpers.BuildUrl(
                "api/brigade-rating/export.xlsx",
                ("from", ResolveExportFrom(key, year, month, today)?.ToString("yyyy-MM-dd")),
                ("to", ResolveExportTo(key, year, month, today)?.ToString("yyyy-MM-dd")));

        var response = await api.OpenDownloadAsync(url, HttpContext.RequestAborted);
        if (response is null)
        {
            return StatusCode(502);
        }

        Response.RegisterForDispose(response);
        if (!response.IsSuccessStatusCode)
        {
            return StatusCode((int)response.StatusCode);
        }

        var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        var contentType = response.Content.Headers.ContentType?.ToString()
            ?? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "Анализ Остановок.xlsx";
        return File(stream, contentType, fileName);
    }

    public async Task<IActionResult> OnPostResolveRemarkAsync(
        Guid remarkId,
        bool isResolved,
        string? period,
        DateOnly? from,
        DateOnly? to,
        int? year,
        int? month)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var result = await api.PatchAsync<StoppageRemarkItem>(
            $"api/brigade-rating/remarks/{remarkId}/resolved",
            new SetRemarkResolvedRequest { IsResolved = isResolved },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error ?? "Не удалось обновить пункт.";
        }

        return RedirectToPage(new { period, from, to, year, month });
    }

    public async Task<IActionResult> OnGetStoppageAsync(Guid id)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var result = await api.GetAsync<BrigadeStoppageDetail>(
            $"api/brigade-rating/stoppages/{id}",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return NotFound();
        }

        return new JsonResult(result.Data, CrownApiClient.JsonOptions);
    }

    private static string NormalizePeriod(string? period) =>
        (period ?? "month").Trim().ToLowerInvariant() switch
        {
            "today" => "today",
            "year" => "year",
            "custom" => "custom",
            _ => "month"
        };

    private static IReadOnlyList<int> BuildYears(int earliest, int todayYear)
    {
        var from = Math.Min(earliest, todayYear);
        if (from > todayYear)
        {
            from = todayYear;
        }

        return Enumerable.Range(from, todayYear - from + 1).Reverse().ToList();
    }

    private static DateOnly? ResolveExportFrom(string period, int? year, int? month, DateOnly today)
    {
        return period switch
        {
            "today" => today,
            "year" => new DateOnly(year is >= 2000 and <= 2100 ? year.Value : today.Year, 1, 1),
            "month" => new DateOnly(
                year is >= 2000 and <= 2100 ? year.Value : today.Year,
                month is >= 1 and <= 12 ? month.Value : today.Month,
                1),
            _ => null
        };
    }

    private static DateOnly? ResolveExportTo(string period, int? year, int? month, DateOnly today)
    {
        if (period == "today")
        {
            return today;
        }

        if (period == "year")
        {
            return new DateOnly(year is >= 2000 and <= 2100 ? year.Value : today.Year, 12, 31);
        }

        if (period == "month")
        {
            var start = new DateOnly(
                year is >= 2000 and <= 2100 ? year.Value : today.Year,
                month is >= 1 and <= 12 ? month.Value : today.Month,
                1);
            var end = start.AddMonths(1).AddDays(-1);
            return end > today && start.Year == today.Year && start.Month == today.Month ? today : end;
        }

        return null;
    }
}
