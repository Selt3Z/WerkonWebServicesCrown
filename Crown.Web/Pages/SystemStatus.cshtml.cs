using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
[RequestSizeLimit(512 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 512 * 1024 * 1024)]
public sealed class SystemStatusModel(CrownApiClient api) : PageModel
{
    public SystemStatusResponse? Status { get; private set; }

    public string? Error { get; private set; }

    [BindProperty]
    public string SelectedBackupPath { get; set; } = string.Empty;

    [BindProperty]
    public string RestoreConfirmation { get; set; } = string.Empty;

    [BindProperty]
    public string SessionsConfirmation { get; set; } = string.Empty;

    [BindProperty]
    public string PowerConfirmation { get; set; } = string.Empty;

    [BindProperty]
    public IFormFile? BackupUpload { get; set; }

    public string? SupportReportText { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var loaded = await LoadAsync();
        if (loaded is not null)
        {
            return loaded;
        }

        SupportReportText = TempData["SupportReport"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostBackupDumpAsync()
    {
        var result = await api.PostAsync<ManualBackupResponse>("api/system/backup/run-dump", null, HttpContext.RequestAborted);
        return FlashBackupAndRedirect(result);
    }

    public async Task<IActionResult> OnPostBackupResticAsync()
    {
        var result = await api.PostAsync<ManualBackupResponse>("api/system/backup/run-restic", null, HttpContext.RequestAborted);
        return FlashBackupAndRedirect(result);
    }

    public async Task<IActionResult> OnPostRestoreAsync()
    {
        var result = await api.PostAsync<object>(
            "api/system/restore-backup",
            new { RelativePath = SelectedBackupPath, Confirmation = RestoreConfirmation },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok)
        {
            TempData["Ok"] = "База восстановлена. При необходимости войдите снова.";
        }
        else
        {
            TempData["Warn"] = result.Error;
        }

        return Redirect("/system");
    }

    public async Task<IActionResult> OnPostUploadBackupAsync()
    {
        if (BackupUpload is null || BackupUpload.Length == 0)
        {
            TempData["Warn"] = "Выберите файл резервной копии.";
            return Redirect("/system");
        }

        var result = await api.UploadFileAsync<BackupFileInfo>(
            "api/system/backup/upload",
            BackupUpload,
            ct: HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok)
        {
            TempData["Ok"] = "Файл резервной копии загружен.";
            if (!string.IsNullOrWhiteSpace(result.Data?.RelativePath))
            {
                TempData["SelectedBackupPath"] = result.Data.RelativePath;
            }
        }
        else
        {
            TempData["Warn"] = result.Error;
        }

        return Redirect("/system");
    }

    public async Task<IActionResult> OnPostDownloadBackupAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedBackupPath))
        {
            TempData["Warn"] = "Выберите файл.";
            return Redirect("/system");
        }

        var url = UiHelpers.BuildUrl("api/system/backup/download", ("path", SelectedBackupPath));
        return await ProxyDownloadAsync(url, "backup.dump.gz");
    }

    public async Task<IActionResult> OnPostDownloadLogsAsync(string service)
    {
        var url = UiHelpers.BuildUrl($"api/system/logs/{Uri.EscapeDataString(service)}", ("mb", "2"));
        return await ProxyDownloadAsync(url, $"{service}-logs.txt");
    }

    public async Task<IActionResult> OnPostResetSessionsAsync()
    {
        var result = await api.PostAsync<ResetSessionsResponse>(
            "api/system/sessions/reset-all",
            new { Confirmation = SessionsConfirmation },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok && result.Data?.Success == true)
        {
            TempData["Ok"] = result.Data.Message
                ?? $"Сессии сброшены ({result.Data.UsersAffected}).";
        }
        else
        {
            TempData["Warn"] = result.Error ?? result.Data?.Message ?? "Не удалось сбросить сессии.";
        }

        return Redirect("/system");
    }

    public async Task<IActionResult> OnPostSupportAsync()
    {
        var result = await api.PostAsync<SupportContactResponse>(
            "api/system/support/contact",
            null,
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok && !string.IsNullOrWhiteSpace(result.Data?.ReportText))
        {
            TempData["SupportReport"] = result.Data.ReportText;
            TempData["Ok"] = "Отчёт для поддержки собран.";
        }
        else
        {
            TempData["Warn"] = result.Error ?? result.Data?.Message ?? "Не удалось собрать отчёт.";
        }

        return Redirect("/system");
    }

    public async Task<IActionResult> OnPostPowerAsync(string action)
    {
        var path = action switch
        {
            "restart" => "api/system/power/restart-stack",
            "reboot" => "api/system/power/reboot-host",
            "shutdown" => "api/system/power/shutdown-host",
            _ => null
        };

        if (path is null)
        {
            TempData["Warn"] = "Неизвестное действие.";
            return Redirect("/system");
        }

        var result = await api.PostAsync<HostPowerResponse>(
            path,
            new { Confirmation = PowerConfirmation },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok && (result.Data is null || result.Data.Started || string.IsNullOrWhiteSpace(result.Data.ErrorCode)))
        {
            TempData["Ok"] = result.Data?.Message ?? "Команда отправлена.";
        }
        else
        {
            TempData["Warn"] = result.Error ?? result.Data?.Message ?? "Команда не выполнена.";
        }

        return Redirect("/system");
    }

    private async Task<IActionResult?> LoadAsync()
    {
        var result = await api.GetAsync<SystemStatusResponse>("api/system/status", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return null;
        }

        Status = result.Data;
        if (TempData["SelectedBackupPath"] is string selected)
        {
            SelectedBackupPath = selected;
        }
        else if (string.IsNullOrWhiteSpace(SelectedBackupPath)
                 && Status?.AvailableBackups.Count > 0)
        {
            SelectedBackupPath = Status.AvailableBackups[0].RelativePath;
        }

        return null;
    }

    private IActionResult FlashBackupAndRedirect(ApiResult<ManualBackupResponse> result)
    {
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Ok && result.Data?.Success != false)
        {
            TempData["Ok"] = string.IsNullOrWhiteSpace(result.Data?.Message)
                ? "Резервная копия запущена / создана."
                : result.Data.Message;
        }
        else
        {
            TempData["Warn"] = result.Error ?? result.Data?.Message ?? "Ошибка резервного копирования.";
        }

        return Redirect("/system");
    }

    private async Task<IActionResult> ProxyDownloadAsync(string apiUrl, string fallbackName)
    {
        var response = await api.OpenDownloadAsync(apiUrl, HttpContext.RequestAborted);
        if (response is null)
        {
            TempData["Warn"] = "Не удалось скачать файл.";
            return Redirect("/system");
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            TempData["Warn"] = "Файл недоступен.";
            return Redirect("/system");
        }

        HttpContext.Response.RegisterForDispose(response);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"')
            ?? fallbackName;
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/octet-stream";
        var stream = await response.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
        return File(stream, contentType, fileName);
    }
}
