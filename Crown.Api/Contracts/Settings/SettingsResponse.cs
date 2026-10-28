namespace Crown.Contracts.Settings;

public sealed class SettingsResponse
{
    public string TimeZoneId { get; set; } = string.Empty;

    public string Theme { get; set; } = "light";

    public string CurrencyCode { get; set; } = "RUB";

    public int CurrencyDecimalPlaces { get; set; } = 2;

    public bool HideArchivedByDefault { get; set; } = true;

    public bool DispatcherCanHardDelete { get; set; }

    public bool WarehouseEnabled { get; set; }

    public int AuditRetentionDays { get; set; } = 30;

    public int ListPageSize { get; set; } = 30;
}