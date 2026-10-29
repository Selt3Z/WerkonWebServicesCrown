namespace Crown.Contracts.Requests;

public sealed class AttachmentPreviewResponse
{
    /// <summary>pdf | image | text | table | archive | unsupported</summary>
    public string Kind { get; set; } = "unsupported";

    public string FileName { get; set; } = string.Empty;

    public string ContentType { get; set; } = string.Empty;

    public string? Text { get; set; }

    public List<AttachmentPreviewSheet> Sheets { get; set; } = [];

    public List<AttachmentArchiveEntry> Entries { get; set; } = [];

    public string? Message { get; set; }
}

public sealed class AttachmentPreviewSheet
{
    public string Name { get; set; } = string.Empty;

    public List<List<string>> Rows { get; set; } = [];
}

public sealed class AttachmentArchiveEntry
{
    public string Path { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public long? SizeBytes { get; set; }
}
