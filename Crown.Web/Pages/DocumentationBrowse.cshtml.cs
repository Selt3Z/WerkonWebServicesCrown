using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class DocumentationBrowseModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid LibraryId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? FolderId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty]
    public string? NewFolderName { get; set; }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    public DocumentationBrowse? Browse { get; private set; }

    public DocumentationInboxInfo? Inbox { get; private set; }

    /// <summary>Папки раздела для select «Переместить» (PathLabel).</summary>
    public List<DocumentationFolderItem> MoveTargets { get; private set; } = [];

    public bool IsAdmin => User.IsInRole(AppRoles.Administrator);

    public string? Error { get; private set; }

    public string? Info { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync();
    }

    public async Task<IActionResult> OnPostSyncInboxAsync()
    {
        if (!IsAdmin)
        {
            return Forbid();
        }

        var result = await api.PostAsync<DocumentationInboxSyncResult>(
            $"api/documentation/libraries/{LibraryId}/sync-inbox",
            body: null,
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Импорт с диска не выполнен.";
            await LoadAsync();
            return Page();
        }

        var r = result.Data;
        Info =
            $"С диска: импортировано {r.Imported}, обновлено {r.Updated}, пропущено {r.Skipped}, ошибок {r.Failed}"
            + (r.FoldersCreated > 0 ? $", папок создано {r.FoldersCreated}" : "")
            + ".";
        if (r.Messages.Count > 0)
        {
            Info += " " + string.Join(" ", r.Messages.Take(5));
        }

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostCreateFolderAsync()
    {
        var result = await api.PostAsync<DocumentationFolderItem>(
            $"api/documentation/libraries/{LibraryId}/folders",
            new { Name = NewFolderName, ParentFolderId = FolderId },
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

        return RedirectToPage(new { LibraryId, FolderId, Q });
    }

    public async Task<IActionResult> OnPostUploadAsync()
    {
        if (UploadFile is null || UploadFile.Length == 0)
        {
            Error = "Файл не передан.";
            await LoadAsync();
            return Page();
        }

        var url = UiHelpers.BuildUrl(
            $"api/documentation/libraries/{LibraryId}/files",
            ("folderId", FolderId?.ToString()));
        var result = await api.UploadFileAsync<DocumentationFileItem>(
            url,
            UploadFile,
            "file",
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

        return RedirectToPage(new { LibraryId, FolderId, Q });
    }

    // targetFolderId — не путать с FolderId (текущая папка в URL); form binder case-insensitive.
    public async Task<IActionResult> OnPostDeleteFolderAsync(Guid targetFolderId)
    {
        var result = await api.DeleteAsync(
            $"api/documentation/folders/{targetFolderId}",
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

        return RedirectToPage(new { LibraryId, FolderId, Q });
    }

    public async Task<IActionResult> OnPostDeleteFileAsync(Guid targetFileId)
    {
        var result = await api.DeleteAsync(
            $"api/documentation/files/{targetFileId}",
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

        return RedirectToPage(new { LibraryId, FolderId, Q });
    }

    public async Task<IActionResult> OnPostMoveFolderAsync(Guid targetFolderId, Guid? moveToParentId)
    {
        var result = await api.PostAsync<DocumentationFolderItem>(
            $"api/documentation/folders/{targetFolderId}/move",
            new { TargetParentFolderId = moveToParentId },
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

        return RedirectToPage(new { LibraryId, FolderId, Q });
    }

    public async Task<IActionResult> OnPostMoveFileAsync(Guid targetFileId, Guid? moveToFolderId)
    {
        var result = await api.PostAsync<DocumentationFileItem>(
            $"api/documentation/files/{targetFileId}/move",
            new { TargetFolderId = moveToFolderId },
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

        return RedirectToPage(new { LibraryId, FolderId, Q });
    }

    public async Task<IActionResult> OnGetDownloadAsync(Guid fileId, bool inline = false)
    {
        var url = UiHelpers.BuildUrl(
            $"api/documentation/files/{fileId}",
            ("inline", inline ? "true" : null));
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
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "file";
        if (inline)
        {
            return File(stream, contentType);
        }

        return File(stream, contentType, fileName);
    }

    private async Task<IActionResult> LoadAsync()
    {
        var url = UiHelpers.BuildUrl(
            $"api/documentation/libraries/{LibraryId}",
            ("folderId", FolderId?.ToString()),
            ("q", string.IsNullOrWhiteSpace(Q) ? null : Q.Trim()));
        var result = await api.GetAsync<DocumentationBrowse>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error ??= result.Error ?? "Раздел не найден.";
            return Page();
        }

        Browse = result.Data;

        if (Browse.CanWrite)
        {
            var folders = await api.GetAsync<List<DocumentationFolderItem>>(
                $"api/documentation/libraries/{LibraryId}/folders",
                HttpContext.RequestAborted);
            if (folders.Ok && folders.Data is not null)
            {
                MoveTargets = folders.Data;
            }
        }

        if (IsAdmin)
        {
            var inbox = await api.GetAsync<DocumentationInboxInfo>(
                $"api/documentation/libraries/{LibraryId}/inbox",
                HttpContext.RequestAborted);
            if (inbox.Ok)
            {
                Inbox = inbox.Data;
            }
        }

        return Page();
    }
}
