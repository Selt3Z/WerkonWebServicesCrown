namespace Crown.Domain.Entities;

/// <summary>Файл в каталоге документации (диск — тот же ATTACHMENTS_HOST_PATH).</summary>
public sealed class DocumentationFile
{
    public Guid Id { get; set; }

    /// <summary>Каталог (после миграции обязателен).</summary>
    public Guid? LibraryId { get; set; }

    /// <summary>Legacy OwnerUserId старого раздела.</summary>
    public Guid? OwnerUserId { get; set; }

    /// <summary>null — корень каталога.</summary>
    public Guid? FolderId { get; set; }

    public DocumentationFolder? Folder { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = "application/octet-stream";

    public long SizeBytes { get; set; }

    /// <summary>Относительный путь внутри каталога вложений (docs/...).</summary>
    public string StoragePath { get; set; } = string.Empty;

    /// <summary>
    /// Ключ из share-inbox: относительный путь файла внутри папки подразделения
    /// (для идемпотентного повторного синка). null — обычная загрузка через UI.
    /// </summary>
    public string? InboxSourceKey { get; set; }

    public DateTimeOffset UploadedAt { get; set; }

    public Guid? UploadedByUserId { get; set; }

    /// <summary>null — живой файл; иначе в корзине до автоочистки.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedByUserId { get; set; }
}
