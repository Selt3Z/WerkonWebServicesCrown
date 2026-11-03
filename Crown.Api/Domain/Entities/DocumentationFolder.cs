namespace Crown.Domain.Entities;

/// <summary>Папка в разделе документации пользователя.</summary>
public sealed class DocumentationFolder
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public Guid? ParentFolderId { get; set; }

    public DocumentationFolder? ParentFolder { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public List<DocumentationFolder> Children { get; set; } = [];

    public List<DocumentationFile> Files { get; set; } = [];
}
