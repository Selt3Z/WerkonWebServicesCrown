using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class AdminFeedbackDetailModel(CrownApiClient api, AppTime appTime) : PageModel
{
    public FeedbackDetail? Item { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var result = await api.GetAsync<FeedbackDetail>($"api/feedback/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Status == 404)
        {
            return NotFound();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить обращение.";
            return Page();
        }

        Item = result.Data;
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(Guid id, Guid attachmentId)
    {
        var url = $"api/feedback/{id}/attachments/{attachmentId}";
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
        var contentType = response.Content.Headers.ContentType?.ToString()
            ?? "application/octet-stream";
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? "file";
        return File(stream, contentType, fileName);
    }

    public string FmtWhen(DateTimeOffset utc) =>
        appTime.Format(utc);
}
