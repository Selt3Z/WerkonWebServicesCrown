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

    public const string ListPageSize = "ListPageSize";
}