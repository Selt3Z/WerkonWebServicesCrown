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

    /// <summary>Путь на хосте к каталогу вложений (ATTACHMENTS_HOST_PATH).</summary>
    public string AttachmentsHostPath { get; set; } = string.Empty;

    /// <summary>Путь внутри контейнера api (/data/attachments).</summary>
    public string AttachmentsContainerPath { get; set; } = "/data/attachments";

    /// <summary>Старое место на хосте (чтение), если задан ATTACHMENTS_LEGACY_HOST_PATH.</summary>
    public string? AttachmentsLegacyHostPath { get; set; }

    /// <summary>true, если legacy-диск реально отличается от основного.</summary>
    public bool AttachmentsLegacyActive { get; set; }
}