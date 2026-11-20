using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>Импорт остановок из xlsx с сопоставлением столбцов.</summary>
[Authorize]
public sealed class BrigadeRatingStoppagesImportModel(CrownApiClient api) : PageModel
{
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanViewBrigadeRatingClaim)?.Value == "1"
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    public bool CanEnter =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    public IActionResult OnGet()
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        if (!CanEnter)
        {
            return Forbid();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostImportPreviewAsync(IFormFile? file)
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        if (file is null || file.Length == 0)
        {
            return new JsonResult(new { error = "file_required", message = "Выберите файл." }) { StatusCode = 400 };
        }

        var result = await api.UploadFileAsync<SpreadsheetImportPreview>(
            "api/brigade-rating/stoppages/import/preview",
            file,
            "file",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return new JsonResult(new { error = "preview_failed", message = result.Error ?? "Ошибка чтения файла." })
            {
                StatusCode = 400
            };
        }

        return new JsonResult(result.Data, CrownApiClient.JsonOptions);
    }

    public async Task<IActionResult> OnPostImportCommitAsync([FromBody] SpreadsheetImportCommitRequest body)
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        var result = await api.PostAsync<SpreadsheetImportCommitResult>(
            "api/brigade-rating/stoppages/import/commit",
            body,
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return new JsonResult(new { error = "commit_failed", message = result.Error ?? "Ошибка импорта." })
            {
                StatusCode = 400
            };
        }

        return new JsonResult(result.Data, CrownApiClient.JsonOptions);
    }
}
