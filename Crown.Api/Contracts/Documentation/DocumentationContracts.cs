namespace Crown.Contracts.Documentation;

public sealed class DocumentationSectionResponse
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool CanWrite { get; set; }
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
}

/// <summary>Глобальный поиск по файлам/папкам всех доступных разделов.</summary>
public sealed class DocumentationGlobalSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public List<DocumentationGlobalSearchHitResponse> Hits { get; set; } = [];
}

public sealed class DocumentationGlobalSearchHitResponse
{
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public bool IsFolder { get; set; }
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? PathLabel { get; set; }
    public Guid? FolderId { get; set; }
    public long? SizeBytes { get; set; }
    public DateTimeOffset? At { get; set; }
}

public sealed class DocumentationFolderItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    /// <summary>Путь для результатов поиска (пусто в обычном листинге).</summary>
    public string? PathLabel { get; set; }
}

public sealed class DocumentationFileItemResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid? FolderId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
    /// <summary>Путь папки для результатов поиска.</summary>
    public string? PathLabel { get; set; }
}

public sealed class DocumentationBrowseResponse
{
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public Guid? ParentFolderId { get; set; }
    public bool CanWrite { get; set; }
    /// <summary>Админ удаляет безвозвратно; подразделение/диспетчер — в корзину.</summary>
    public bool DeletesPermanently { get; set; }
    /// <summary>Корзина раздела: владелец-подразделение или админ.</summary>
    public bool CanViewTrash { get; set; }
    /// <summary>Очистка корзины — только админ.</summary>
    public bool CanClearTrash { get; set; }
    /// <summary>Непустой — режим поиска по разделу подразделения.</summary>
    public string? SearchQuery { get; set; }
    public List<DocumentationFolderItemResponse> Folders { get; set; } = [];
    public List<DocumentationFileItemResponse> Files { get; set; } = [];
}

public sealed class DocumentationTrashItemResponse
{
    public Guid Id { get; set; }
    public bool IsFolder { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? SizeBytes { get; set; }
    public DateTimeOffset DeletedAt { get; set; }
    public string? PathLabel { get; set; }
}

public sealed class DocumentationTrashResponse
{
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public bool CanClearTrash { get; set; }
    public int RetentionDays { get; set; }
    public List<DocumentationTrashItemResponse> Items { get; set; } = [];
}

public sealed class ClearDocumentationTrashRequest
{
    /// <summary>Должно быть TRASH.</summary>
    public string? Confirmation { get; set; }
}

public sealed class CreateDocumentationFolderRequest
{
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
}

public sealed class DocumentationAdminUserResponse
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool DocumentationEnabled { get; set; }
}

public sealed class UpdateDocumentationSettingsRequest
{
    public List<DocumentationAdminUserToggle> Users { get; set; } = [];
}

public sealed class DocumentationAdminUserToggle
{
    public Guid UserId { get; set; }
    public bool DocumentationEnabled { get; set; }
}
