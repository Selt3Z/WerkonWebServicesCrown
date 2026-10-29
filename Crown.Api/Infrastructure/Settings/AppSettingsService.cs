using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Crown.Contracts.Settings;
using Crown.Domain.Entities;
using Crown.Infrastructure.Attachments;
using Crown.Infrastructure.Pdf;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Time;

namespace Crown.Infrastructure.Settings;

public sealed class AppSettingsService
{
    private const int MaxLogoBytes = 512 * 1024;
    private const int MinListPageSize = 10;
    private const int MaxListPageSize = 100;
    private const int MinAuditRetentionDays = 7;
    private const int MaxAuditRetentionDays = 3650;

    private static readonly HashSet<string> AllowedCurrencyCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "RUB",
        "USD",
        "EUR",
        "JPY"
    };

    private static readonly HashSet<string> AllowedLogoContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/webp"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly AppDbContext _dbContext;
    private readonly AppTimeZone _appTimeZone;
    private readonly IConfiguration _configuration;
    private readonly AttachmentStorageOptions _attachments;

    public AppSettingsService(
        AppDbContext dbContext,
        AppTimeZone appTimeZone,
        IConfiguration configuration,
        IOptions<AttachmentStorageOptions>? attachments = null)
    {
        _dbContext = dbContext;
        _appTimeZone = appTimeZone;
        _configuration = configuration;
        _attachments = attachments?.Value ?? new AttachmentStorageOptions();
    }

    public async Task<SettingsResponse> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var currencyCode = await GetCurrencyCodeAsync(cancellationToken);
        var hostPath = string.IsNullOrWhiteSpace(_attachments.HostPath)
            ? _attachments.RootPath
            : _attachments.HostPath.Trim();
        var legacyHost = string.IsNullOrWhiteSpace(_attachments.LegacyHostPath)
            ? null
            : _attachments.LegacyHostPath.Trim();
        var legacyActive = !string.IsNullOrWhiteSpace(legacyHost)
            && !string.Equals(hostPath, legacyHost, StringComparison.OrdinalIgnoreCase);

        var logo = await LoadOrganizationLogoAsync(cancellationToken);
        string? logoDataUrl = null;
        if (logo is not null
            && !string.IsNullOrWhiteSpace(logo.ContentType)
            && !string.IsNullOrWhiteSpace(logo.DataBase64))
        {
            logoDataUrl = $"data:{logo.ContentType};base64,{logo.DataBase64}";
        }

        return new SettingsResponse
        {
            TimeZoneId = await GetTimeZoneIdAsync(cancellationToken),
            Theme = await GetThemeAsync(cancellationToken),
            CurrencyCode = currencyCode,
            CurrencyDecimalPlaces = GetCurrencyDecimalPlaces(currencyCode),
            HideArchivedByDefault = await GetHideArchivedByDefaultAsync(cancellationToken),
            DispatcherCanHardDelete = await GetDispatcherCanHardDeleteAsync(cancellationToken),
            WarehouseEnabled = await GetWarehouseEnabledAsync(cancellationToken),
            AuditRetentionDays = await GetAuditRetentionDaysAsync(cancellationToken),
            ListPageSize = await GetListPageSizeAsync(cancellationToken),
            BrandLogoMode = await GetBrandLogoModeAsync(cancellationToken),
            HasCustomLogo = logoDataUrl is not null,
            LogoDataUrl = logoDataUrl,
            AttachmentsHostPath = hostPath,
            AttachmentsContainerPath = _attachments.RootPath,
            AttachmentsLegacyHostPath = legacyHost,
            AttachmentsLegacyActive = legacyActive
        };
    }

    public async Task<SettingsResponse> UpdateSettingsAsync(
        UpdateSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(request.TimeZoneId))
        {
            await SetTimeZoneIdAsync(request.TimeZoneId, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.Theme))
        {
            await SetThemeAsync(request.Theme, cancellationToken);
        }

        // CurrencyCode is host-managed (CurrencyCode env / scripts/set-currency.sh), not editable via UI.

        if (request.HideArchivedByDefault.HasValue)
        {
            await SetHideArchivedByDefaultAsync(request.HideArchivedByDefault.Value, cancellationToken);
        }

        if (request.DispatcherCanHardDelete.HasValue)
        {
            await SetDispatcherCanHardDeleteAsync(request.DispatcherCanHardDelete.Value, cancellationToken);
        }

        if (request.WarehouseEnabled.HasValue)
        {
            await SetWarehouseEnabledAsync(request.WarehouseEnabled.Value, cancellationToken);
        }

        if (request.AuditRetentionDays.HasValue)
        {
            await SetAuditRetentionDaysAsync(request.AuditRetentionDays.Value, cancellationToken);
        }

        if (request.ListPageSize.HasValue)
        {
            await SetListPageSizeAsync(request.ListPageSize.Value, cancellationToken);
        }

        if (!string.IsNullOrWhiteSpace(request.BrandLogoMode))
        {
            await SetBrandLogoModeAsync(request.BrandLogoMode, cancellationToken);
        }

        return await GetSettingsAsync(cancellationToken);
    }

    public async Task<string> GetBrandLogoModeAsync(CancellationToken cancellationToken = default)
    {
        var stored = await GetSettingValueAsync(AppSettingKeys.BrandLogoMode, cancellationToken);
        return NormalizeBrandLogoMode(stored);
    }

    public async Task<string> SetBrandLogoModeAsync(string mode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBrandLogoMode(mode);
        await SaveSettingAsync(AppSettingKeys.BrandLogoMode, normalized, cancellationToken);
        return normalized;
    }

    /// <summary>
    /// Байты своего логотипа для шапки/логина — только если режим custom и файл есть.
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType)?> TryGetActiveBrandLogoAsync(
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(await GetBrandLogoModeAsync(cancellationToken), "custom", StringComparison.Ordinal))
        {
            return null;
        }

        var logo = await LoadOrganizationLogoAsync(cancellationToken);
        if (logo is null
            || string.IsNullOrWhiteSpace(logo.ContentType)
            || string.IsNullOrWhiteSpace(logo.DataBase64))
        {
            return null;
        }

        try
        {
            var bytes = Convert.FromBase64String(logo.DataBase64);
            if (bytes.Length == 0)
            {
                return null;
            }

            return (bytes, logo.ContentType);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static string NormalizeBrandLogoMode(string? mode) =>
        string.Equals(mode?.Trim(), "custom", StringComparison.OrdinalIgnoreCase)
            ? "custom"
            : "standard";

    public Task<string> GetCurrencyCodeAsync(CancellationToken cancellationToken = default)
    {
        var fromConfig = _configuration["CurrencyCode"];
        if (!string.IsNullOrWhiteSpace(fromConfig))
        {
            return Task.FromResult(NormalizeCurrencyCode(fromConfig));
        }

        return GetCurrencyCodeFromStoreAsync(cancellationToken);
    }

    private async Task<string> GetCurrencyCodeFromStoreAsync(CancellationToken cancellationToken)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.CurrencyCode, cancellationToken);
        return NormalizeCurrencyCode(storedValue);
    }
    public async Task<bool> GetHideArchivedByDefaultAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.HideArchivedByDefault, cancellationToken);
        return ParseBool(storedValue, AppSettingsDefaults.HideArchivedByDefault);
    }
    public async Task<bool> GetDispatcherCanHardDeleteAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.DispatcherCanHardDelete, cancellationToken);
        return ParseBool(storedValue, AppSettingsDefaults.DispatcherCanHardDelete);
    }

    public async Task<bool> GetWarehouseEnabledAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.WarehouseEnabled, cancellationToken);
        return ParseBool(storedValue, AppSettingsDefaults.WarehouseEnabled);
    }
    public async Task<int> GetAuditRetentionDaysAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.AuditRetentionDays, cancellationToken);
        return ParseBoundedInt(storedValue, AppSettingsDefaults.AuditRetentionDays, MinAuditRetentionDays, MaxAuditRetentionDays);
    }

    public async Task<int> GetListPageSizeAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.ListPageSize, cancellationToken);
        return ParseBoundedInt(storedValue, AppSettingsDefaults.ListPageSize, MinListPageSize, MaxListPageSize);
    }
    public Task<string> SetCurrencyCodeAsync(string currencyCode, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeCurrencyCode(currencyCode);
        if (!AllowedCurrencyCodes.Contains(normalized))
        {
            throw new ArgumentException("Unsupported currency code.", nameof(currencyCode));
        }

        return SaveAndReturnAsync(AppSettingKeys.CurrencyCode, normalized, cancellationToken);
    }
    public Task<bool> SetHideArchivedByDefaultAsync(bool hideArchived, CancellationToken cancellationToken = default) =>
        SaveAndReturnBoolAsync(AppSettingKeys.HideArchivedByDefault, hideArchived, cancellationToken);
    public Task<bool> SetDispatcherCanHardDeleteAsync(bool allowed, CancellationToken cancellationToken = default) =>
        SaveAndReturnBoolAsync(AppSettingKeys.DispatcherCanHardDelete, allowed, cancellationToken);

    public Task<bool> SetWarehouseEnabledAsync(bool enabled, CancellationToken cancellationToken = default) =>
        SaveAndReturnBoolAsync(AppSettingKeys.WarehouseEnabled, enabled, cancellationToken);
    public Task<int> SetAuditRetentionDaysAsync(int days, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBoundedInt(days, MinAuditRetentionDays, MaxAuditRetentionDays, nameof(days));
        return SaveAndReturnIntAsync(AppSettingKeys.AuditRetentionDays, normalized, cancellationToken);
    }

    public Task<int> SetListPageSizeAsync(int pageSize, CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeBoundedInt(pageSize, MinListPageSize, MaxListPageSize, nameof(pageSize));
        return SaveAndReturnIntAsync(AppSettingKeys.ListPageSize, normalized, cancellationToken);
    }
    public async Task<string> GetTimeZoneIdAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.TimeZone, cancellationToken);

        if (!string.IsNullOrWhiteSpace(storedValue))
        {
            return storedValue;
        }

        return _configuration[AppSettingKeys.TimeZone] ?? "Europe/Moscow";
    }

    public async Task<string> GetThemeAsync(CancellationToken cancellationToken = default)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.Theme, cancellationToken);

        if (!string.IsNullOrWhiteSpace(storedValue))
        {
            return NormalizeTheme(storedValue);
        }

        return NormalizeTheme(_configuration[AppSettingKeys.Theme]);
    }

    public async Task<string> SetTimeZoneIdAsync(string timeZoneId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId.Trim(), out _))
        {
            throw new ArgumentException("Invalid time zone id.", nameof(timeZoneId));
        }

        var normalizedId = timeZoneId.Trim();
        await SaveSettingAsync(AppSettingKeys.TimeZone, normalizedId, cancellationToken);
        _appTimeZone.SetTimeZoneId(normalizedId);

        return normalizedId;
    }

    public async Task<string> SetThemeAsync(string theme, CancellationToken cancellationToken = default)
    {
        var normalizedTheme = NormalizeTheme(theme);
        await SaveSettingAsync(AppSettingKeys.Theme, normalizedTheme, cancellationToken);
        return normalizedTheme;
    }

    public async Task<OrganizationProfileResponse> GetOrganizationProfileAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await LoadOrganizationProfileAsync(cancellationToken);
        var logo = await LoadOrganizationLogoAsync(cancellationToken);

        return MapOrganizationProfileResponse(profile, logo);
    }

    public async Task<OrganizationProfileResponse> UpdateOrganizationProfileAsync(
        UpdateOrganizationProfileRequest request,
        CancellationToken cancellationToken = default)
    {
        var profile = new OrganizationProfile
        {
            OrganizationName = request.OrganizationName.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim(),
            Address = request.Address.Trim(),
            Inn = request.Inn.Trim(),
            Kpp = request.Kpp.Trim(),
            Ogrn = request.Ogrn.Trim(),
            BankAccount = request.BankAccount.Trim(),
            Bik = request.Bik.Trim(),
        };

        await SaveSettingAsync(
            AppSettingKeys.OrganizationProfile,
            JsonSerializer.Serialize(profile, JsonOptions),
            cancellationToken);

        var logo = await LoadOrganizationLogoAsync(cancellationToken);
        return MapOrganizationProfileResponse(profile, logo);
    }
    public async Task<OrganizationProfileResponse> UploadOrganizationLogoAsync(
        UploadOrganizationLogoRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ContentType)
            || !AllowedLogoContentTypes.Contains(request.ContentType.Trim()))
        {
            throw new ArgumentException("Unsupported logo content type.");
        }

        if (string.IsNullOrWhiteSpace(request.DataBase64))
        {
            throw new ArgumentException("Logo data is required.");
        }

        byte[] logoBytes;

        try
        {
            logoBytes = Convert.FromBase64String(request.DataBase64.Trim());
        }
        catch (FormatException)
        {
            throw new ArgumentException("Invalid logo data.");
        }

        if (logoBytes.Length == 0 || logoBytes.Length > MaxLogoBytes)
        {
            throw new ArgumentException($"Logo size must be between 1 byte and {MaxLogoBytes} bytes.");
        }

        var detectedContentType = DetectImageContentType(logoBytes);

        if (detectedContentType is null)
        {
            throw new ArgumentException("Logo data is not a valid PNG, JPEG, or WebP image.");
        }

        if (!string.Equals(detectedContentType, request.ContentType.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Logo content type does not match the actual image format.");
        }

        var logo = new OrganizationLogoData
        {
            ContentType = request.ContentType.Trim(),
            DataBase64 = request.DataBase64.Trim()
        };

        await SaveSettingAsync(
            AppSettingKeys.OrganizationLogo,
            JsonSerializer.Serialize(logo, JsonOptions),
            cancellationToken);

        var profile = await LoadOrganizationProfileAsync(cancellationToken);
        return MapOrganizationProfileResponse(profile, logo);
    }

    public async Task<OrganizationProfileResponse> DeleteOrganizationLogoAsync(
        CancellationToken cancellationToken = default)
    {
        var setting = await _dbContext.AppSettings
            .FirstOrDefaultAsync(x => x.Key == AppSettingKeys.OrganizationLogo, cancellationToken);

        if (setting is not null)
        {
            _dbContext.AppSettings.Remove(setting);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        var profile = await LoadOrganizationProfileAsync(cancellationToken);
        return MapOrganizationProfileResponse(profile, logo: null);
    }

    public async Task<OrganizationDocumentInfo> GetOrganizationDocumentInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var profile = await LoadOrganizationProfileAsync(cancellationToken);
        var logo = await LoadOrganizationLogoAsync(cancellationToken);

        byte[]? logoBytes = null;

        if (logo is not null)
        {
            try
            {
                logoBytes = Convert.FromBase64String(logo.DataBase64);
            }
            catch (FormatException)
            {
                logoBytes = null;
            }
        }

        return new OrganizationDocumentInfo
        {
            OrganizationName = profile.OrganizationName,
            Phone = profile.Phone,
            Email = profile.Email,
            Address = profile.Address,
            Inn = profile.Inn,
            Kpp = profile.Kpp,
            Ogrn = profile.Ogrn,
            BankAccount = profile.BankAccount,
            Bik = profile.Bik,
            LogoBytes = logoBytes
        };
    }

    /// <summary>
    /// Detects the image format from magic bytes. Returns null when the data
    /// is not one of the supported formats (PNG, JPEG, WebP).
    /// </summary>
    private static string? DetectImageContentType(byte[] data)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return "image/png";
        }

        // JPEG: FF D8 FF
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        // WebP: "RIFF" .... "WEBP"
        if (data.Length >= 12
            && data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46
            && data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
        {
            return "image/webp";
        }

        return null;
    }

    private async Task<OrganizationProfile> LoadOrganizationProfileAsync(CancellationToken cancellationToken)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.OrganizationProfile, cancellationToken);

        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return new OrganizationProfile();
        }

        return JsonSerializer.Deserialize<OrganizationProfile>(storedValue, JsonOptions)
            ?? new OrganizationProfile();
    }

    private async Task<OrganizationLogoData?> LoadOrganizationLogoAsync(CancellationToken cancellationToken)
    {
        var storedValue = await GetSettingValueAsync(AppSettingKeys.OrganizationLogo, cancellationToken);

        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return null;
        }

        return JsonSerializer.Deserialize<OrganizationLogoData>(storedValue, JsonOptions);
    }

    private static OrganizationProfileResponse MapOrganizationProfileResponse(
        OrganizationProfile profile,
        OrganizationLogoData? logo)
    {
        string? logoDataUrl = null;

        if (logo is not null
            && !string.IsNullOrWhiteSpace(logo.ContentType)
            && !string.IsNullOrWhiteSpace(logo.DataBase64))
        {
            logoDataUrl = $"data:{logo.ContentType};base64,{logo.DataBase64}";
        }

        return new OrganizationProfileResponse
        {
            OrganizationName = profile.OrganizationName,
            Phone = profile.Phone,
            Email = profile.Email,
            Address = profile.Address,
            Inn = profile.Inn,
            Kpp = profile.Kpp,
            Ogrn = profile.Ogrn,
            BankAccount = profile.BankAccount,
            Bik = profile.Bik,
            HasLogo = logoDataUrl is not null,
            LogoDataUrl = logoDataUrl
        };
    }
    private async Task<string?> GetSettingValueAsync(string key, CancellationToken cancellationToken)
    {
        return await _dbContext.AppSettings
            .AsNoTracking()
            .Where(x => x.Key == key)
            .Select(x => x.Value)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task SaveSettingAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await _dbContext.AppSettings
            .FirstOrDefaultAsync(x => x.Key == key, cancellationToken);

        if (setting is null)
        {
            setting = new AppSetting
            {
                Key = key,
                Value = value
            };
            _dbContext.AppSettings.Add(setting);
        }
        else
        {
            setting.Value = value;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private static string NormalizeTheme(string? theme) => AppThemeNames.Normalize(theme);

    private static string NormalizeCurrencyCode(string? currencyCode) =>
        string.IsNullOrWhiteSpace(currencyCode)
            ? AppSettingsDefaults.CurrencyCode
            : currencyCode.Trim().ToUpperInvariant();

    private static int GetCurrencyDecimalPlaces(string currencyCode) =>
        string.Equals(currencyCode, "JPY", StringComparison.OrdinalIgnoreCase)
            ? 0
            : AppSettingsDefaults.CurrencyDecimalPlaces;
    private static bool ParseBool(string? value, bool defaultValue) =>
        bool.TryParse(value, out var parsed) ? parsed : defaultValue;

    private static int ParseBoundedInt(string? value, int defaultValue, int min, int max)
    {
        if (!int.TryParse(value, out var parsed))
        {
            return defaultValue;
        }

        return Math.Clamp(parsed, min, max);
    }

    private static int NormalizeBoundedInt(int value, int min, int max, string paramName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Value must be between {min} and {max}.");
        }

        return value;
    }

    private async Task<string> SaveAndReturnAsync(string key, string value, CancellationToken cancellationToken)
    {
        await SaveSettingAsync(key, value, cancellationToken);
        return value;
    }

    private async Task<int> SaveAndReturnIntAsync(string key, int value, CancellationToken cancellationToken)
    {
        await SaveSettingAsync(key, value.ToString(), cancellationToken);
        return value;
    }

    private async Task<bool> SaveAndReturnBoolAsync(string key, bool value, CancellationToken cancellationToken)
    {
        await SaveSettingAsync(key, value.ToString(), cancellationToken);
        return value;
    }
}
