namespace Crown.Infrastructure.Attachments;

public sealed class AttachmentStorageOptions
{
    public const string SectionName = "Attachments";

    /// <summary>Каталог записи/чтения внутри контейнера, например /data/attachments.</summary>
    public string RootPath { get; set; } = "/data/attachments";

    /// <summary>
    /// Путь на хосте (для UI/доков). Сам по себе диск не меняет — меняется bind-mount
    /// ATTACHMENTS_HOST_PATH в .env + docker compose up -d.
    /// </summary>
    public string HostPath { get; set; } = string.Empty;

    /// <summary>
    /// Доп. корень только для чтения (старое место после смены диска).
    /// В контейнере обычно /data/attachments-legacy.
    /// </summary>
    public string? LegacyRootPath { get; set; }

    /// <summary>Хост-путь legacy (для UI).</summary>
    public string? LegacyHostPath { get; set; }

    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}
