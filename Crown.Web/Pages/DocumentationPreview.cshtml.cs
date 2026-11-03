using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class DocumentationPreviewModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid FileId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Back { get; set; }

    public AttachmentPreviewResponse? Preview { get; private set; }

    public string? Error { get; private set; }

    public string DownloadUrl => $"/documentation/preview?handler=Download&fileId={FileId}";

    public string InlineUrl => $"/documentation/preview?handler=Download&fileId={FileId}&inline=true";

    public string BackUrl => string.IsNullOrWhiteSpace(Back) ? "/documentation" : Back;

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await api.GetAsync<AttachmentPreviewResponse>(
            $"api/documentation/files/{FileId}/preview",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось открыть превью.";
            return Page();
        }

        Preview = result.Data;
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var result = await api.DeleteAsync($"api/documentation/files/{FileId}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error ?? "Не удалось удалить файл.";
            return Redirect(BackUrl);
        }

        TempData["Ok"] = "Файл удалён.";
        return Redirect(BackUrl);
    }

    public async Task<IActionResult> OnGetDownloadAsync(bool inline = false)
    {
        var url = UiHelpers.BuildUrl(
            $"api/documentation/files/{FileId}",
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
}
