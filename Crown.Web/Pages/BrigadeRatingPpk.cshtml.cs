using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>Список ППК (профиль Супервайзинга), без вкладок других видов проверок.</summary>
[Authorize]
public sealed class BrigadeRatingPpkModel(CrownApiClient api, AppTime time) : PageModel
{
    private const BrigadeInspectionKind Kind = BrigadeInspectionKind.Ppk;

    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanViewBrigadeRatingClaim)?.Value == "1"
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

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

    public List<BrigadeInspectionListItem> Items { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public IReadOnlyList<int> AvailableYears { get; private set; } = [];

    public string AddUrl => "/brigade-rating/stoppage?inspectionKind=Ppk";

    public string PeriodUrl(string period) =>
        UiHelpers.BuildUrl(
            "/brigade-rating/ppk",
            ("period", period),
            ("year", period == "year" ? Year?.ToString() : null));

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        Period = NormalizePeriod(Period);
        var today = time.GetToday();
        if (Period == "custom" && From is null && To is null)
        {
            Period = "month";
        }

        var (items, hasMore, page, error, challenge) = await LoadAsync(0);
        if (challenge)
        {
            return Challenge();
        }

        if (page is not null)
        {
            Period = page.Period;
            From = page.PeriodStart;
            To = page.PeriodEnd;
            Year = page.PeriodStart.Year;
            Month = page.PeriodStart.Month;
            AvailableYears = BuildYears(page.EarliestYear, today.Year);
        }
        else
        {
            AvailableYears = [today.Year];
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

        Period = NormalizePeriod(Period);
        var (items, hasMore, _, _, challenge) = await LoadAsync(skip);
        if (challenge)
        {
            return Challenge();
        }

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        return Partial("_InspectionListRows", (Kind, CanEnter, items));
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        Period = NormalizePeriod(Period);
        var today = time.GetToday();
        var url = Period == "custom"
            ? UiHelpers.BuildUrl(
                "api/brigade-rating/inspections/export.xlsx",
                ("kind", "Ppk"),
                ("from", From?.ToString("yyyy-MM-dd")),
                ("to", To?.ToString("yyyy-MM-dd")))
            : UiHelpers.BuildUrl(
                "api/brigade-rating/inspections/export.xlsx",
                ("kind", "Ppk"),
                ("from", ResolveExportFrom(Period, Year, Month, today)?.ToString("yyyy-MM-dd")),
                ("to", ResolveExportTo(Period, Year, Month, today)?.ToString("yyyy-MM-dd")));

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
            ?? "ППК.xlsx";
        return File(stream, contentType, fileName);
    }

    private async Task<(List<BrigadeInspectionListItem> Items, bool HasMore, BrigadeInspectionsPage? Page, string? Error, bool Challenge)> LoadAsync(
        int skip)
    {
        var url = UiHelpers.BuildUrl(
            "api/brigade-rating/inspections",
            ("kind", Kind.ToString()),
            ("period", Period == "custom" ? null : Period),
            ("from", Period == "custom" ? From?.ToString("yyyy-MM-dd") : null),
            ("to", Period == "custom" ? To?.ToString("yyyy-MM-dd") : null),
            ("year", Period is "month" or "year" ? Year?.ToString() : null),
            ("month", Period == "month" ? Month?.ToString() : null),
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<BrigadeInspectionsPage>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return ([], false, null, null, true);
        }

        if (!result.Ok || result.Data is null)
        {
            return ([], false, null, result.Error ?? "Не удалось загрузить ППК.", false);
        }

        return (result.Data.Items, result.Data.HasMore, result.Data, null, false);
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

    private static DateOnly? ResolveExportFrom(string period, int? year, int? month, DateOnly today) =>
        period switch
        {
            "today" => today,
            "year" => new DateOnly(year is >= 2000 and <= 2100 ? year.Value : today.Year, 1, 1),
            "month" => new DateOnly(
                year is >= 2000 and <= 2100 ? year.Value : today.Year,
                month is >= 1 and <= 12 ? month.Value : today.Month,
                1),
            _ => null
        };

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
