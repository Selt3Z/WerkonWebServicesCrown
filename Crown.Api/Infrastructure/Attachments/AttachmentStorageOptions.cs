namespace Crown.Infrastructure.Attachments;

public sealed class AttachmentStorageOptions
{
    public const string SectionName = "Attachments";

    /// <summary>Каталог на диске контейнера, например /data/attachments.</summary>
    public string RootPath { get; set; } = "/data/attachments";

    public long MaxFileBytes { get; set; } = 20 * 1024 * 1024;
}
