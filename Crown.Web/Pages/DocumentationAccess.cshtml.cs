using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NoContent = Crown.Web.Services.NoContent;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class DocumentationAccessModel(CrownApiClient api) : PageModel
{
    public List<DocumentationLibrary> Libraries { get; private set; } = [];

    public List<DocumentationAclUser> Users { get; private set; } = [];

    public List<DocumentationLibraryMember> Members { get; private set; } = [];

    public DocumentationLegacySources? Legacy { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? LibraryId { get; set; }

    [BindProperty]
    public Guid? MigrateOwnerUserId { get; set; }

    [BindProperty]
    public Guid? MigrateLibraryId { get; set; }

    /// <summary>Форма: UserId отмеченных участников.</summary>
    [BindProperty]
    public List<Guid> MemberUserIds { get; set; } = [];

    /// <summary>Форма: UserId с правом записи.</summary>
    [BindProperty]
    public List<Guid> WriteUserIds { get; set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync();
    }

    public async Task<IActionResult> OnPostSaveMembersAsync()
    {
        if (LibraryId is null)
        {
            Error = "Выберите каталог.";
            await LoadAsync();
            return Page();
        }

        var members = MemberUserIds
            .Distinct()
            .Select(id => new
            {
                UserId = id,
                CanWrite = WriteUserIds.Contains(id)
            })
            .ToList();

        var result = await api.PutAsync<NoContent>(
            $"api/documentation/libraries/{LibraryId}/members",
            new { Members = members },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error ?? "Не удалось сохранить участников.";
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Участники сохранены.";
        return RedirectToPage(new { LibraryId });
    }

    public async Task<IActionResult> OnPostMigrateAsync()
    {
        if (MigrateOwnerUserId is null || MigrateLibraryId is null)
        {
            Error = "Выберите источник и каталог для переноса.";
            await LoadAsync();
            return Page();
        }

        var result = await api.PostAsync<MigrateDocumentationResult>(
            "api/documentation/admin/migrate",
            new
            {
                Items = new[]
                {
                    new { OwnerUserId = MigrateOwnerUserId.Value, LibraryId = MigrateLibraryId.Value }
                }
            },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Перенос не выполнен.";
            await LoadAsync();
            return Page();
        }

        var r = result.Data;
        TempData["Ok"] =
            $"Перенесено: папок {r.FoldersUpdated}, файлов {r.FilesUpdated}, участников +{r.MembersAdded}.";
        return RedirectToPage(new { LibraryId });
    }

    private async Task<IActionResult> LoadAsync()
    {
        var libraries = await api.GetAsync<List<DocumentationLibrary>>(
            "api/documentation/libraries",
            HttpContext.RequestAborted);
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
        if (LibraryId is null && Libraries.Count > 0)
        {
            LibraryId = Libraries[0].Id;
        }

        var users = await api.GetAsync<List<DocumentationAclUser>>(
            "api/documentation/admin/users",
            HttpContext.RequestAborted);
        if (users.Ok && users.Data is not null)
        {
            Users = users.Data;
        }

        if (LibraryId is Guid lid)
        {
            var members = await api.GetAsync<List<DocumentationLibraryMember>>(
                $"api/documentation/libraries/{lid}/members",
                HttpContext.RequestAborted);
            if (members.Ok && members.Data is not null)
            {
                Members = members.Data;
            }
        }

        var legacy = await api.GetAsync<DocumentationLegacySources>(
            "api/documentation/admin/legacy-sources",
            HttpContext.RequestAborted);
        if (legacy.Ok)
        {
            Legacy = legacy.Data;
        }

        return Page();
    }
}
