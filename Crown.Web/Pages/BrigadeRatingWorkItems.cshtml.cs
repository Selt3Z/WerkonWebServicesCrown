using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class BrigadeRatingWorkItemsModel(CrownApiClient api) : PageModel
{
    public const int PageSize = 50;
    public const string ClearConfirmationWord = "ОЧИСТИТЬ";

    private bool CanOperate =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    public bool CanClear => User.IsInRole(AppRoles.Administrator);

    public List<StoppageWorkItem> Items { get; private set; } = [];
    public int Total { get; private set; }
    public int PageIndex { get; private set; } = 1;
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true, Name = "p")]
    public int PageNum { get; set; } = 1;

    public string? Error { get; private set; }

    [BindProperty]
    public string? NewText { get; set; }

    [BindProperty]
    public Guid? EditId { get; set; }

    [BindProperty]
    public string? EditText { get; set; }

    [BindProperty]
    public bool EditIsActive { get; set; } = true;

    [BindProperty]
    public IFormFile? ImportFile { get; set; }

    [BindProperty]
    public string? ClearConfirmation { get; set; }

    [BindProperty]
    public string? ClearPassword { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanOperate)
        {
            return Forbid();
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
        if (!CanOperate)
        {
            return Forbid();
        }

        var text = (NewText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Error = "Укажите текст работы.";
            await LoadAsync();
            return Page();
        }

        var result = await api.PostAsync<StoppageWorkItem>(
            "api/brigade-rating/work-items",
            new SaveStoppageWorkItemRequest { Text = text, IsActive = true },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Работа добавлена.";
        return RedirectToPage(ListRoute());
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!CanOperate)
        {
            return Forbid();
        }

        if (EditId is null)
        {
            Error = "Не указана работа.";
            await LoadAsync();
            return Page();
        }

        var text = (EditText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Error = "Укажите текст работы.";
            await LoadAsync();
            return Page();
        }

        var result = await api.PutAsync<StoppageWorkItem>(
            $"api/brigade-rating/work-items/{EditId}",
            new SaveStoppageWorkItemRequest { Text = text, IsActive = EditIsActive },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Сохранено.";
        return RedirectToPage(ListRoute());
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (!CanOperate)
        {
            return Forbid();
        }

        var result = await api.DeleteAsync($"api/brigade-rating/work-items/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Удалено.";
        return RedirectToPage(ListRoute());
    }

    public async Task<IActionResult> OnPostImportAsync()
    {
        if (!CanOperate)
        {
            return Forbid();
        }

        if (ImportFile is null || ImportFile.Length == 0)
        {
            Error = "Выберите файл (xlsx, xls, csv, txt, doc, docx).";
            await LoadAsync();
            return Page();
        }

        var result = await api.UploadFileAsync<CatalogLinesImportResult>(
            "api/brigade-rating/work-items/import",
            ImportFile,
            "file",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось импортировать.";
            await LoadAsync();
            return Page();
        }

        var r = result.Data;
        TempData["Ok"] =
            $"Импорт: добавлено {r.Imported}, пропущено дублей {r.SkippedDuplicates} (распознано строк {r.Parsed}).";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnGetExportAsync()
    {
        if (!CanOperate)
        {
            return Forbid();
        }

        var response = await api.OpenDownloadAsync(
            "api/brigade-rating/work-items/export.xlsx",
            HttpContext.RequestAborted);
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
            ?? "work-items.xlsx";
        return File(stream, contentType, fileName);
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        if (!CanClear)
        {
            return Forbid();
        }

        var result = await api.PostAsync<ClearBrigadeCatalogResult>(
            "api/brigade-rating/work-items/clear",
            new ClearBrigadeCatalogRequest
            {
                Confirmation = ClearConfirmation,
                Password = ClearPassword
            },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            TempData["Warn"] = result.Error ?? "Не удалось очистить справочник.";
            return RedirectToPage();
        }

        TempData["Ok"] = $"Справочник очищен. Удалено: {result.Data.Deleted}.";
        return RedirectToPage();
    }

    private object ListRoute() => new { q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim(), p = PageNum > 1 ? PageNum : (int?)null };

    private async Task LoadAsync()
    {
        PageIndex = PageNum < 1 ? 1 : PageNum;
        var skip = (PageIndex - 1) * PageSize;
        var q = string.IsNullOrWhiteSpace(Q) ? null : Q.Trim();
        var url = "api/brigade-rating/work-items?take=" + PageSize
                  + "&skip=" + skip
                  + (q is null ? "" : "&q=" + Uri.EscapeDataString(q));

        var result = await api.GetAsync<StoppageWorkItemsList>(url, HttpContext.RequestAborted);
        if (result.Ok && result.Data is not null)
        {
            Items = result.Data.Items;
            Total = result.Data.Total;
        }
        else if (!string.IsNullOrEmpty(result.Error))
        {
            Error ??= result.Error;
        }
    }
}
