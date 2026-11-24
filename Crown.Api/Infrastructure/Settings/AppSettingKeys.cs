namespace Crown.Infrastructure.Settings;

public static class AppSettingKeys
{
    public const string TimeZone = "AppTimeZone";

    public const string Theme = "AppTheme";

    public const string OrganizationProfile = "OrganizationProfile";

    public const string OrganizationLogo = "OrganizationLogo";

    /// <summary>standard | custom — какой логотип в шапке/логине.</summary>
    public const string BrandLogoMode = "BrandLogoMode";

    public const string CurrencyCode = "CurrencyCode";

    public const string HideArchivedByDefault = "HideArchivedByDefault";

    public const string DispatcherCanHardDelete = "DispatcherCanHardDelete";

    public const string WarehouseEnabled = "WarehouseEnabled";

    public const string AuditRetentionDays = "AuditRetentionDays";

    /// <summary>Сколько дней файлы/папки документации лежат в корзине.</summary>
    public const string DocumentationTrashRetentionDays = "DocumentationTrashRetentionDays";

    public const string ListPageSize = "ListPageSize";

    /// <summary>Утреннее окно подачи плановых заявок бригадой (TimeOnly HH:mm).</summary>
    public const string BrigadeMorningWindowStart = "BrigadeMorningWindowStart";
    public const string BrigadeMorningWindowEnd = "BrigadeMorningWindowEnd";

    /// <summary>Вечернее окно подачи плановых заявок бригадой (TimeOnly HH:mm).</summary>
    public const string BrigadeEveningWindowStart = "BrigadeEveningWindowStart";
    public const string BrigadeEveningWindowEnd = "BrigadeEveningWindowEnd";
}