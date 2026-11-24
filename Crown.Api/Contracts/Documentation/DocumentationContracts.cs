namespace Crown.Contracts.Documentation;

public sealed class DocumentationLibraryResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool CanWrite { get; set; }
    public bool CanManageMembers { get; set; }
    public int FileCount { get; set; }
    public long TotalSizeBytes { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string? InboxFolderKey { get; set; }
}

public sealed class CreateDocumentationLibraryRequest
{
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? InboxFolderKey { get; set; }
}

public sealed class UpdateDocumentationLibraryRequest
{
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string? InboxFolderKey { get; set; }
}

public sealed class DocumentationLibraryMemberResponse
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool CanWrite { get; set; }
}

public sealed class UpdateLibraryMembersRequest
{
    public List<LibraryMemberToggle> Members { get; set; } = [];
}

public sealed class LibraryMemberToggle
{
    public Guid UserId { get; set; }
    public bool CanWrite { get; set; }
}

/// <summary>Глобальный поиск по файлам/папкам доступных каталогов.</summary>
public sealed class DocumentationGlobalSearchResponse
{
    public string Query { get; set; } = string.Empty;
    public List<DocumentationGlobalSearchHitResponse> Hits { get; set; } = [];
}

public sealed class DocumentationGlobalSearchHitResponse
{
    public Guid LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
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
    public Guid LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public Guid? ParentFolderId { get; set; }
    public bool CanWrite { get; set; }
    /// <summary>Админ удаляет безвозвратно; остальные с записью — в корзину.</summary>
    public bool DeletesPermanently { get; set; }
    /// <summary>Корзина: админ или участник с CanWrite.</summary>
    public bool CanViewTrash { get; set; }
    /// <summary>Очистка корзины — только админ.</summary>
    public bool CanClearTrash { get; set; }
    /// <summary>Непустой — режим поиска по каталогу.</summary>
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
    public Guid LibraryId { get; set; }
    public string LibraryName { get; set; } = string.Empty;
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

public sealed class MoveDocumentationFolderRequest
{
    /// <summary>null — корень каталога.</summary>
    public Guid? TargetParentFolderId { get; set; }
}

public sealed class MoveDocumentationFileRequest
{
    /// <summary>null — корень каталога.</summary>
    public Guid? TargetFolderId { get; set; }
}

/// <summary>Кандидаты для ACL каталога (настройки).</summary>
public sealed class DocumentationAclUserResponse
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string? BrigadeWorkshop { get; set; }
    public bool IsActive { get; set; }
}

public sealed class DocumentationInboxInfoResponse
{
    public Guid LibraryId { get; set; }
    public string FolderKey { get; set; } = string.Empty;
    public string HostInboxPath { get; set; } = string.Empty;
    public string RelativeInboxPath { get; set; } = string.Empty;
    public int PendingFileCount { get; set; }
    public long MaxInboxFileBytes { get; set; }
}

public sealed class DocumentationInboxSyncResponse
{
    public Guid? LibraryId { get; set; }
    public string? FolderKey { get; set; }
    public string HostInboxPath { get; set; } = string.Empty;
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public int FoldersCreated { get; set; }
    public int PendingBefore { get; set; }
    public List<string> Messages { get; set; } = [];
}
