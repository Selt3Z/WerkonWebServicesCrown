using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class BrigadeRatingModel(CrownApiClient api) : PageModel
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
    public DateOnly? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateOnly? To { get; set; }

    public BrigadeRatingDashboard? Dashboard { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var today = DateOnly.FromDateTime(DateTime.Now);
        From ??= new DateOnly(today.Year, today.Month, 1);
        To ??= today;

        var url = UiHelpers.BuildUrl(
            "api/brigade-rating/dashboard",
            ("from", From?.ToString("yyyy-MM-dd")),
            ("to", To?.ToString("yyyy-MM-dd")));

        var result = await api.GetAsync<BrigadeRatingDashboard>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить рейтинг бригад.";
            return Page();
        }

        Dashboard = result.Data;
        From = Dashboard.PeriodStart;
        To = Dashboard.PeriodEnd;
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(DateOnly? from, DateOnly? to)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var url = UiHelpers.BuildUrl(
            "api/brigade-rating/export.xlsx",
            ("from", from?.ToString("yyyy-MM-dd")),
            ("to", to?.ToString("yyyy-MM-dd")));
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

    public async Task<IActionResult> OnPostResolveRemarkAsync(Guid remarkId, bool isResolved, DateOnly? from, DateOnly? to)
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

        return RedirectToPage(new { from, to });
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
}
