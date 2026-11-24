namespace Crown.Infrastructure.Settings;

public static class AppSettingsDefaults
{
    public const string CurrencyCode = "RUB";

    public const int CurrencyDecimalPlaces = 2;

    public const bool HideArchivedByDefault = true;

    public const bool DispatcherCanHardDelete = false;

    public const bool WarehouseEnabled = true;

    public const int AuditRetentionDays = 30;

    public const int DocumentationTrashRetentionDays = 7;

    public const int ListPageSize = 30;

    /// <summary>Утро: [04:00, 07:00).</summary>
    public static readonly TimeOnly BrigadeMorningWindowStart = new(4, 0);
    public static readonly TimeOnly BrigadeMorningWindowEnd = new(7, 0);

    /// <summary>Вечер: [15:00, 18:00).</summary>
    public static readonly TimeOnly BrigadeEveningWindowStart = new(15, 0);
    public static readonly TimeOnly BrigadeEveningWindowEnd = new(18, 0);
}