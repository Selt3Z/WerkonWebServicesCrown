namespace Crown.Domain.Entities;

/// <summary>Фиксированный каталог документации (создаёт админ).</summary>
public sealed class DocumentationLibrary
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Ключ папки в docs-inbox (часто бывший логин подразделения).</summary>
    public string? InboxFolderKey { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public List<DocumentationLibraryMember> Members { get; set; } = [];
}
