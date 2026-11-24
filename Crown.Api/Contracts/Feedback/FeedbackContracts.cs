namespace Crown.Contracts.Feedback;

public sealed class CreateFeedbackRequest
{
    public string Body { get; set; } = string.Empty;

    public string? SectionKey { get; set; }

    public string? SectionLabel { get; set; }
}

public sealed class FeedbackListItemResponse
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? SectionKey { get; set; }
    public string? SectionLabel { get; set; }
    public string BodyPreview { get; set; } = string.Empty;
    public int AttachmentCount { get; set; }
}

public sealed class FeedbackDetailResponse
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string? SectionKey { get; set; }
    public string? SectionLabel { get; set; }
    public string Body { get; set; } = string.Empty;
    public List<FeedbackAttachmentResponse> Attachments { get; set; } = [];
}

public sealed class FeedbackAttachmentResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
}
