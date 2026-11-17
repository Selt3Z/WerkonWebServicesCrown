using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class BrigadeRatingWorkItemsModel(CrownApiClient api) : PageModel
{
    private bool CanOperate =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    public List<StoppageWorkItem> Items { get; private set; } = [];

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
        return RedirectToPage();
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
        return RedirectToPage();
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
        return RedirectToPage();
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

    private async Task LoadAsync()
    {
        var result = await api.GetAsync<List<StoppageWorkItem>>(
            "api/brigade-rating/work-items",
            HttpContext.RequestAborted);
        if (result.Ok && result.Data is not null)
        {
            Items = result.Data;
        }
        else if (!string.IsNullOrEmpty(result.Error))
        {
            Error ??= result.Error;
        }
    }
}
