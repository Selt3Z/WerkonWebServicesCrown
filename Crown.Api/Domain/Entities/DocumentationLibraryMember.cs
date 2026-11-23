namespace Crown.Domain.Entities;

/// <summary>Доступ пользователя к каталогу. Членство = чтение; CanWrite = загрузка/правка.</summary>
public sealed class DocumentationLibraryMember
{
    public Guid Id { get; set; }

    public Guid LibraryId { get; set; }

    public DocumentationLibrary? Library { get; set; }

    public Guid UserId { get; set; }

    public bool CanWrite { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
