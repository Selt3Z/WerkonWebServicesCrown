using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class SettingsModel(CrownApiClient api, AppTime appTime) : PageModel
{
    public const string ThemeCookieName = "Crown.Theme";
    private const int MaxLogoBytes = 512 * 1024;

    [BindProperty]
    public SettingsResponse Input { get; set; } = new();

    /// <summary>Новый файл логотипа (опционально) — проверка как в Ratchet.</summary>
    [BindProperty]
    public IFormFile? LogoFile { get; set; }

    public IReadOnlyList<TimeZoneInfo> AvailableTimeZones { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await api.GetAsync<SettingsResponse>("api/settings", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            AvailableTimeZones = TimeZoneDisplayHelper.GetOrderedTimeZones();
            return Page();
        }

        Input = result.Data;
        appTime.SetTimeZoneId(Input.TimeZoneId);
        AvailableTimeZones = TimeZoneDisplayHelper.GetOrderedTimeZones(Input.TimeZoneId);
        SetThemeCookie(Input.Theme);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        AvailableTimeZones = TimeZoneDisplayHelper.GetOrderedTimeZones(Input.TimeZoneId);

        var mode = string.Equals(Input.BrandLogoMode, "custom", StringComparison.OrdinalIgnoreCase)
            ? "custom"
            : "standard";

        if (mode == "custom")
        {
            if (LogoFile is { Length: > 0 })
            {
                var uploadError = await UploadLogoAsync(LogoFile);
                if (uploadError is not null)
                {
                    Error = uploadError;
                    return Page();
                }

                Input.HasCustomLogo = true;
            }
            else if (!Input.HasCustomLogo)
            {
                Error = "Для своего логотипа загрузите PNG, JPEG или WEBP (до 512 КБ).";
                return Page();
            }
        }

        var result = await api.PutAsync<SettingsResponse>(
            "api/settings",
            new UpdateSettingsRequest
            {
                TimeZoneId = Input.TimeZoneId,
                Theme = Input.Theme,
                HideArchivedByDefault = Input.HideArchivedByDefault,
                DispatcherCanHardDelete = Input.DispatcherCanHardDelete,
                WarehouseEnabled = Input.WarehouseEnabled,
                AuditRetentionDays = Input.AuditRetentionDays,
                ListPageSize = Input.ListPageSize,
                BrandLogoMode = mode
            },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        if (result.Data is not null)
        {
            Input = result.Data;
        }

        if (!string.IsNullOrWhiteSpace(Input.TimeZoneId))
        {
            appTime.SetTimeZoneId(Input.TimeZoneId);
        }

        SetThemeCookie(Input.Theme);
        TempData["Ok"] = "Настройки сохранены.";
        return Redirect("/settings");
    }

    private async Task<string?> UploadLogoAsync(IFormFile file)
    {
        if (file.Length == 0 || file.Length > MaxLogoBytes)
        {
            return "Размер логотипа должен быть от 1 байта до 512 КБ.";
        }

        await using var stream = file.OpenReadStream();
        using var buffer = new MemoryStream((int)file.Length);
        await stream.CopyToAsync(buffer, HttpContext.RequestAborted);
        var bytes = buffer.ToArray();

        var contentType = NormalizeContentType(file.ContentType);
        // Как в Ratchet: тип с клиента должен совпасть с magic bytes на API.
        // Если браузер прислал пустой/странный тип — пробуем по сигнатуре.
        contentType ??= DetectContentType(bytes);
        if (contentType is null)
        {
            return "Логотип должен быть в формате PNG, JPEG или WEBP.";
        }

        var upload = await api.PutAsync<OrganizationProfileResponse>(
            "api/settings/organization/logo",
            new UploadOrganizationLogoRequest
            {
                ContentType = contentType,
                DataBase64 = Convert.ToBase64String(bytes)
            },
            HttpContext.RequestAborted);

        if (upload.IsUnauthorized)
        {
            return "Сессия истекла — войдите снова.";
        }

        if (!upload.Ok)
        {
            return upload.Error ?? "Не удалось загрузить логотип.";
        }

        return null;
    }

    private static string? NormalizeContentType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var t = raw.Trim().ToLowerInvariant();
        return t switch
        {
            "image/jpg" => "image/jpeg",
            "image/jpeg" or "image/png" or "image/webp" => t,
            _ => null
        };
    }

    private static string? DetectContentType(byte[] data)
    {
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return "image/png";
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 12
            && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }

    private void SetThemeCookie(string? theme)
    {
        var normalized = string.IsNullOrWhiteSpace(theme) ? "light" : theme.Trim().ToLowerInvariant();
        Response.Cookies.Append(
            ThemeCookieName,
            normalized,
            new CookieOptions
            {
                Path = "/",
                HttpOnly = false,
                IsEssential = true,
                MaxAge = TimeSpan.FromDays(400),
                SameSite = SameSiteMode.Lax
            });
    }
}
