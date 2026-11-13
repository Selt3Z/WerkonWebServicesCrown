using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class BrigadeRatingWorkItemsModel(CrownApiClient api) : PageModel
{
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

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync()
    {
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
            Error = result.Error ?? "Не удалось добавить.";
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Работа добавлена.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (EditId is not Guid id)
        {
            return RedirectToPage();
        }

        var text = (EditText ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            Error = "Укажите текст работы.";
            await LoadAsync();
            return Page();
        }

        var result = await api.PutAsync<StoppageWorkItem>(
            $"api/brigade-rating/work-items/{id}",
            new SaveStoppageWorkItemRequest { Text = text, IsActive = EditIsActive },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error ?? "Не удалось сохранить.";
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Сохранено.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await api.DeleteAsync($"api/brigade-rating/work-items/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error ?? "Не удалось удалить.";
        }
        else
        {
            TempData["Ok"] = "Работа удалена.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var result = await api.GetAsync<List<StoppageWorkItem>>(
            "api/brigade-rating/work-items",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            Items = [];
            return;
        }

        if (!result.Ok)
        {
            Error ??= result.Error;
            Items = [];
            return;
        }

        Items = result.Data ?? [];
    }
}
