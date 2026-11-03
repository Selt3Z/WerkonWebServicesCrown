namespace Crown.Domain.Entities;

/// <summary>Файл в разделе документации (диск — тот же ATTACHMENTS_HOST_PATH).</summary>
public sealed class DocumentationFile
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    /// <summary>null — корень раздела владельца.</summary>
    public Guid? FolderId { get; set; }

    public DocumentationFolder? Folder { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    /// <summary>Относительный путь внутри каталога вложений (docs/...).</summary>
    public string StoragePath { get; set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; set; }

    public Guid? UploadedByUserId { get; set; }
}
