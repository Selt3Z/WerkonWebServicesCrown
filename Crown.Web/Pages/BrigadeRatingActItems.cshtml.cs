using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class BrigadeRatingActItemsModel(CrownApiClient api) : PageModel
{
    public List<StoppageActItem> Items { get; private set; } = [];

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
            Error = "Укажите текст пункта.";
            await LoadAsync();
            return Page();
        }

        var result = await api.PostAsync<StoppageActItem>(
            "api/brigade-rating/act-items",
            new SaveStoppageActItemRequest { Text = text, IsActive = true },
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

        TempData["Ok"] = "Пункт добавлен.";
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
            Error = "Укажите текст пункта.";
            await LoadAsync();
            return Page();
        }

        var result = await api.PutAsync<StoppageActItem>(
            $"api/brigade-rating/act-items/{id}",
            new SaveStoppageActItemRequest { Text = text, IsActive = EditIsActive },
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
        var result = await api.DeleteAsync($"api/brigade-rating/act-items/{id}", HttpContext.RequestAborted);
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
            TempData["Ok"] = "Пункт удалён.";
        }

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var result = await api.GetAsync<List<StoppageActItem>>(
            "api/brigade-rating/act-items",
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
