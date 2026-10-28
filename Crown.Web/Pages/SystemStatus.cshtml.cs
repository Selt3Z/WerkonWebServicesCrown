using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class SystemStatusModel(CrownApiClient api) : PageModel
{
    public SystemStatusResponse? Status { get; private set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await api.GetAsync<SystemStatusResponse>("api/system/status", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        Status = result.Data;
        return Page();
    }

    public async Task<IActionResult> OnPostBackupAsync()
    {
        var result = await api.PostAsync<ManualBackupResponse>("api/system/backup/run-dump", null, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok)
        {
            TempData["Ok"] = string.IsNullOrWhiteSpace(result.Data?.Message)
                ? "Резервная копия создана."
                : result.Data.Message;
        }
        else
        {
            TempData["Warn"] = result.Error;
        }

        return Redirect("/system");
    }
}
