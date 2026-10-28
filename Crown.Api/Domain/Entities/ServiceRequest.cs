namespace Crown.Domain.Entities;

public sealed class ServiceRequest
{
    public long Id { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Снимок ФИО инициатора (остаётся после удаления пользователя).</summary>
    public string InitiatorName { get; set; } = string.Empty;

    /// <summary>Кто подал / от чьего имени; при удалении → null.</summary>
    public Guid? InitiatorUserId { get; set; }

    /// <summary>Снимок ФИО исполнителя.</summary>
    public string ExecutorName { get; set; } = string.Empty;

    /// <summary>Кому направлена заявка (пользователь-инициатор); при удалении → null.</summary>
    public Guid? ExecutorUserId { get; set; }

    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    public ServiceRequestStatus Status { get; set; } = ServiceRequestStatus.Submitted;
    public bool Acknowledged { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Жалоба инициатора (возврат / отклонение / выполнено).</summary>
    public string? ComplaintText { get; set; }
    public DateTimeOffset? ComplaintAt { get; set; }
    public string? ComplaintByName { get; set; }
    public Guid? ComplaintByUserId { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public Guid? LastModifiedByUserId { get; set; }
    public DateTimeOffset? LastModifiedAt { get; set; }
    public bool IsArchived { get; set; }

    /// <summary>Системный xmin Postgres — optimistic concurrency.</summary>
    public uint Xmin { get; set; }

    public List<RequestAttachment> Attachments { get; set; } = [];
}
