using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>Список остановок с периодом и infinite scroll.</summary>
[Authorize]
public sealed class BrigadeRatingStoppagesModel(CrownApiClient api, AppTime time) : PageModel
{
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

    [BindProperty(SupportsGet = true)]
    public Guid? BrigadeUserId { get; set; }

    public List<BrigadeStoppageListItem> Items { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public string? BrigadeFilterName { get; private set; }

    public IReadOnlyList<int> AvailableYears { get; private set; } = [];

    public string PeriodUrl(string period) =>
        UiHelpers.BuildUrl(
            "/brigade-rating/stoppages",
            ("period", period),
            ("year", period == "year" ? Year?.ToString() : null),
            ("brigadeUserId", BrigadeUserId?.ToString()));

    public string ClearBrigadeUrl =>
        UiHelpers.BuildUrl(
            "/brigade-rating/stoppages",
            ("period", Period),
            ("from", Period == "custom" ? From?.ToString("yyyy-MM-dd") : null),
            ("to", Period == "custom" ? To?.ToString("yyyy-MM-dd") : null),
            ("year", Period is "month" or "year" ? Year?.ToString() : null),
            ("month", Period == "month" ? Month?.ToString() : null));

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
        if (BrigadeUserId is Guid bid && bid != Guid.Empty)
        {
            BrigadeFilterName = items.FirstOrDefault(x => x.BrigadeUserId == bid)?.BrigadeName;
        }

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
        return Partial("_StoppageListRows", (CanEnter, items));
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

    public async Task<IActionResult> OnPostResolveRemarkAsync(
        Guid remarkId,
        bool isResolved,
        string? period,
        DateOnly? from,
        DateOnly? to,
        int? year,
        int? month,
        Guid? brigadeUserId)
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

        return RedirectToPage(new { period, from, to, year, month, brigadeUserId });
    }

    private async Task<(List<BrigadeStoppageListItem> Items, bool HasMore, BrigadeStoppagesPage? Page, string? Error, bool Challenge)> LoadAsync(
        int skip)
    {
        var url = UiHelpers.BuildUrl(
            "api/brigade-rating/stoppages",
            ("period", Period == "custom" ? null : Period),
            ("from", Period == "custom" ? From?.ToString("yyyy-MM-dd") : null),
            ("to", Period == "custom" ? To?.ToString("yyyy-MM-dd") : null),
            ("year", Period is "month" or "year" ? Year?.ToString() : null),
            ("month", Period == "month" ? Month?.ToString() : null),
            ("brigadeUserId", BrigadeUserId?.ToString()),
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<BrigadeStoppagesPage>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return ([], false, null, null, true);
        }

        if (!result.Ok || result.Data is null)
        {
            return ([], false, null, result.Error ?? "Не удалось загрузить остановки.", false);
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
}
