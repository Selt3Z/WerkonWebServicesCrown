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

    /// <summary>Дней хранения удалённых файлов/папок документации в корзине.</summary>
    public int DocumentationTrashRetentionDays { get; set; } = 7;

    public int ListPageSize { get; set; } = 30;

    /// <summary>standard | custom</summary>
    public string BrandLogoMode { get; set; } = "standard";

    public bool HasCustomLogo { get; set; }

    /// <summary>data:... превью своего логотипа (если загружен).</summary>
    public string? LogoDataUrl { get; set; }

    /// <summary>Путь на хосте к каталогу вложений (ATTACHMENTS_HOST_PATH).</summary>
    public string AttachmentsHostPath { get; set; } = string.Empty;

    /// <summary>Путь внутри контейнера api (/data/attachments).</summary>
    public string AttachmentsContainerPath { get; set; } = "/data/attachments";

    /// <summary>Старое место на хосте (чтение), если задан ATTACHMENTS_LEGACY_HOST_PATH.</summary>
    public string? AttachmentsLegacyHostPath { get; set; }

    /// <summary>true, если legacy-диск реально отличается от основного.</summary>
    public bool AttachmentsLegacyActive { get; set; }

    /// <summary>Утро: начало окна подачи плановых заявок бригадой (HH:mm).</summary>
    public string BrigadeMorningWindowStart { get; set; } = "04:00";

    /// <summary>Утро: конец окна (не включая).</summary>
    public string BrigadeMorningWindowEnd { get; set; } = "07:00";

    /// <summary>Вечер: начало окна.</summary>
    public string BrigadeEveningWindowStart { get; set; } = "15:00";

    /// <summary>Вечер: конец окна (не включая).</summary>
    public string BrigadeEveningWindowEnd { get; set; } = "18:00";
}