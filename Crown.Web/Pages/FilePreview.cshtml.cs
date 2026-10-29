using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class FilePreviewModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public long RequestId { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid AttachmentId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Back { get; set; }

    public AttachmentPreviewResponse? Preview { get; private set; }

    public string? Error { get; private set; }

    public string DownloadUrl =>
        $"/requests/{RequestId}?handler=Download&attachmentId={AttachmentId}";

    public string InlineUrl =>
        $"/requests/{RequestId}?handler=Download&attachmentId={AttachmentId}&inline=true";

    public string BackUrl =>
        string.IsNullOrWhiteSpace(Back) ? $"/requests/{RequestId}" : Back;

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await api.GetAsync<AttachmentPreviewResponse>(
            $"api/requests/{RequestId}/attachments/{AttachmentId}/preview",
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
}
