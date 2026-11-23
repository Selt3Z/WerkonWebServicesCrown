using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class DocumentationModel(CrownApiClient api) : PageModel
{
    public List<DocumentationLibrary> Libraries { get; private set; } = [];

    public DocumentationGlobalSearch? Search { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string? NewLibraryName { get; set; }

    [BindProperty]
    public string? NewInboxFolderKey { get; set; }

    public bool IsAdmin => User.IsInRole(AppRoles.Administrator);

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync();
    }

    public async Task<IActionResult> OnPostCreateLibraryAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var result = await api.PostAsync<DocumentationLibrary>(
            "api/documentation/libraries",
            new { Name = NewLibraryName, SortOrder = 0, InboxFolderKey = NewInboxFolderKey },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error ?? "Не удалось создать каталог.";
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = $"Каталог «{result.Data!.Name}» создан.";
        return RedirectToPage();
    }

    private async Task<IActionResult> LoadAsync()
    {
        // TempData["Ok"] показывает _Layout — здесь не дублируем.
        var librariesTask = api.GetAsync<List<DocumentationLibrary>>(
            "api/documentation/libraries",
            HttpContext.RequestAborted);

        var q = (Q ?? string.Empty).Trim();
        Task<ApiResult<DocumentationGlobalSearch>>? searchTask = null;
        if (q.Length >= 2)
        {
            searchTask = api.GetAsync<DocumentationGlobalSearch>(
                $"api/documentation/search?q={Uri.EscapeDataString(q)}",
                HttpContext.RequestAborted);
        }

        var libraries = await librariesTask;
        if (libraries.IsUnauthorized)
        {
            return Challenge();
        }

        if (!libraries.Ok || libraries.Data is null)
        {
            Error = libraries.Error;
            return Page();
        }

        Libraries = libraries.Data;

        if (searchTask is not null)
        {
            var search = await searchTask;
            if (search.IsUnauthorized)
            {
                return Challenge();
            }

            if (!search.Ok || search.Data is null)
            {
                Error = search.Error;
            }
            else
            {
                Search = search.Data;
            }
        }

        return Page();
    }
}
