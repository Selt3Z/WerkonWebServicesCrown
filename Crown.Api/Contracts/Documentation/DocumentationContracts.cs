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

public sealed class DocumentationFolderItemResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? ParentFolderId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class DocumentationFileItemResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid? FolderId { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class DocumentationBrowseResponse
{
    public Guid OwnerUserId { get; set; }
    public string OwnerDisplayName { get; set; } = string.Empty;
    public Guid? FolderId { get; set; }
    public string? FolderName { get; set; }
    public Guid? ParentFolderId { get; set; }
    public bool CanWrite { get; set; }
    public List<DocumentationFolderItemResponse> Folders { get; set; } = [];
    public List<DocumentationFileItemResponse> Files { get; set; } = [];
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
