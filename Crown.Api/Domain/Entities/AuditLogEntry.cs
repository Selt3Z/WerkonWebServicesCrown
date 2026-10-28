namespace Crown.Domain.Entities;

public sealed class AuditLogEntry
{
    public Guid Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public Guid? UserId { get; set; }

    public string UserDisplayName { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public Guid EntityId { get; set; }

    public string Summary { get; set; } = string.Empty;

    public string? EntityUrl { get; set; }
}
