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
    public Guid OwnerUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? FolderId { get; set; }

    [BindProperty]
    public string? NewFolderName { get; set; }

    [BindProperty]
    public IFormFile? UploadFile { get; set; }

    public DocumentationBrowse? Browse { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync();
    }

    public async Task<IActionResult> OnPostCreateFolderAsync()
    {
        var result = await api.PostAsync<DocumentationFolderItem>(
            $"api/documentation/sections/{OwnerUserId}/folders",
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

        return RedirectToPage(new { OwnerUserId, FolderId });
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
            $"api/documentation/sections/{OwnerUserId}/files",
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

        return RedirectToPage(new { OwnerUserId, FolderId });
    }

    public async Task<IActionResult> OnPostDeleteFolderAsync(Guid folderId)
    {
        var result = await api.DeleteAsync($"api/documentation/folders/{folderId}", HttpContext.RequestAborted);
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

        return RedirectToPage(new { OwnerUserId, FolderId });
    }

    public async Task<IActionResult> OnPostDeleteFileAsync(Guid fileId)
    {
        var result = await api.DeleteAsync($"api/documentation/files/{fileId}", HttpContext.RequestAborted);
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

        return RedirectToPage(new { OwnerUserId, FolderId });
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
            $"api/documentation/sections/{OwnerUserId}",
            ("folderId", FolderId?.ToString()));
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
        return Page();
    }
}
