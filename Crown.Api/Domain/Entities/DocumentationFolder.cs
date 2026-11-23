namespace Crown.Domain.Entities;

/// <summary>Папка в каталоге документации.</summary>
public sealed class DocumentationFolder
{
    public Guid Id { get; set; }

    /// <summary>Каталог (после миграции обязателен). null — ещё не перенесено из старого раздела.</summary>
    public Guid? LibraryId { get; set; }

    /// <summary>Legacy: старый «раздел = пользователь». После переноса можно оставить для аудита.</summary>
    public Guid? OwnerUserId { get; set; }

    public Guid? ParentFolderId { get; set; }

    public DocumentationFolder? ParentFolder { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>null — живая папка; иначе в корзине до автоочистки.</summary>
    public DateTimeOffset? DeletedAt { get; set; }

    public Guid? DeletedByUserId { get; set; }

    public List<DocumentationFolder> Children { get; set; } = [];

    public List<DocumentationFile> Files { get; set; } = [];
}
