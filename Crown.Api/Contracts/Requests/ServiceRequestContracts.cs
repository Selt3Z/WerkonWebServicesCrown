using Crown.Domain;

namespace Crown.Contracts.Requests;

public sealed class ServiceRequestListItemResponse
{
    public long Id { get; set; }
    public ServiceRequestStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public bool Acknowledged { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
}

public sealed class ServiceRequestDetailResponse
{
    public long Id { get; set; }
    public ServiceRequestStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public bool Acknowledged { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public Guid ServiceDepartmentId { get; set; }
    public string DepartmentName { get; set; } = string.Empty;
    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? RowVersion { get; set; }
    public List<RequestAttachmentResponse> Attachments { get; set; } = [];
}

public sealed class RequestAttachmentResponse
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class ServiceDepartmentResponse
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public sealed class CreateServiceRequestRequest
{
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public Guid ServiceDepartmentId { get; set; }
    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
}

public sealed class TransitionServiceRequestRequest
{
    public string Action { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    public DateTimeOffset? NewDueAt { get; set; }
    public string? RowVersion { get; set; }
    public bool? Acknowledged { get; set; }
}
