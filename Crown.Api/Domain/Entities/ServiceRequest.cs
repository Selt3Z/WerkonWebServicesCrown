namespace Crown.Domain.Entities;

public sealed class ServiceRequest
{
    public long Id { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public Guid ServiceDepartmentId { get; set; }
    public ServiceDepartment? ServiceDepartment { get; set; }
    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Submitted;
    public bool Acknowledged { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? LastModifiedByUserId { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>Системный xmin Postgres — optimistic concurrency.</summary>
    public uint Xmin { get; set; }

    public List<RequestAttachment> Attachments { get; set; } = [];
}
