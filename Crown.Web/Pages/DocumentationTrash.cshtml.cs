using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NoContent = Crown.Web.Services.NoContent;

namespace Crown.Web.Pages;

[Authorize]
public sealed class DocumentationTrashModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid LibraryId { get; set; }

    [BindProperty]
    public string? ClearConfirmation { get; set; }

    public DocumentationTrash? Trash { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync();
    }

    public async Task<IActionResult> OnPostRestoreFolderAsync(Guid itemId)
    {
        var result = await api.PostAsync<NoContent>(
            $"api/documentation/libraries/{LibraryId}/trash/restore-folder/{itemId}",
            null,
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

        TempData["Ok"] = "Папка восстановлена.";
        return RedirectToPage(new { LibraryId });
    }

    public async Task<IActionResult> OnPostRestoreFileAsync(Guid itemId)
    {
        var result = await api.PostAsync<NoContent>(
            $"api/documentation/libraries/{LibraryId}/trash/restore-file/{itemId}",
            null,
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

        TempData["Ok"] = "Файл восстановлен.";
        return RedirectToPage(new { LibraryId });
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        var result = await api.PostAsync<NoContent>(
            $"api/documentation/libraries/{LibraryId}/trash/clear",
            new { Confirmation = ClearConfirmation },
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

        TempData["Ok"] = "Корзина очищена.";
        return RedirectToPage(new { LibraryId });
    }

    private async Task<IActionResult> LoadAsync()
    {
        var result = await api.GetAsync<DocumentationTrash>(
            $"api/documentation/libraries/{LibraryId}/trash",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error ??= result.Error ?? "Корзина недоступна.";
            return Page();
        }

        Trash = result.Data;
        return Page();
    }
}
