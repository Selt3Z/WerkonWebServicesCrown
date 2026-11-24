namespace Crown.Domain.Entities;

/// <summary>Сообщение об ошибке / обращение пользователя.</summary>
public sealed class FeedbackReport
{
    public Guid Id { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Снимок ФИО на момент отправки.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Ключ раздела сайдбара (requests, transport, …); null = не указан.</summary>
    public string? SectionKey { get; set; }

    public string? SectionLabel { get; set; }

    public string Body { get; set; } = string.Empty;

    public List<FeedbackAttachment> Attachments { get; set; } = [];
}
