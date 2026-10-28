using Crown.Domain;

namespace Crown.Contracts.Requests;

public sealed class ServiceRequestListItemResponse
{
    public long Id { get; set; }
    public ServiceRequestStatus Status { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public bool Acknowledged { get; set; }
    public string InitiatorName { get; set; } = string.Empty;
    public Guid? InitiatorUserId { get; set; }
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string ExecutorName { get; set; } = string.Empty;
    public Guid? ExecutorUserId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string BodyPreview { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }
    public bool HasComplaint { get; set; }
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
    public Guid? InitiatorUserId { get; set; }
    public string ExecutorName { get; set; } = string.Empty;
    public Guid? ExecutorUserId { get; set; }
    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    public DateTimeOffset? ReactionAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? RowVersion { get; set; }
    public bool HasComplaint { get; set; }
    public string? ComplaintText { get; set; }
    public DateTimeOffset? ComplaintAt { get; set; }
    public string? ComplaintByName { get; set; }
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

/// <summary>Активный пользователь с ролью Initiator — для выбора исполнителя/фильтров.</summary>
public sealed class RequestAssigneeResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class CreateServiceRequestRequest
{
    public string Phone { get; set; } = string.Empty;
    public DateTimeOffset DueAt { get; set; }

    /// <summary>Админ указывает пользователя-инициатора; остальные — текущий user на сервере.</summary>
    public Guid? InitiatorUserId { get; set; }

    /// <summary>Кому направлена заявка (пользователь с ролью Initiator).</summary>
    public Guid ExecutorUserId { get; set; }

    public string? TransportType { get; set; }
    public string Body { get; set; } = string.Empty;
}

public sealed class TransitionServiceRequestRequest
{
    public string Action { get; set; } = string.Empty;
    public string? ExecutorComment { get; set; }
    /// <summary>Текст заявки — для повторной подачи инициатором после возврата.</summary>
    public string? Body { get; set; }
    public DateTimeOffset? NewDueAt { get; set; }
    public string? RowVersion { get; set; }
    public bool? Acknowledged { get; set; }
}

public sealed class FileComplaintRequest
{
    public string Text { get; set; } = string.Empty;
    public bool Confirmed { get; set; }
}
