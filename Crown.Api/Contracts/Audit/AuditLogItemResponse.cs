namespace Crown.Contracts.Audit;

public sealed class AuditLogItemResponse
{
    public Guid Id { get; set; }

    public DateTime OccurredAtUtc { get; set; }

    public string UserDisplayName { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string EntityType { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public string? EntityUrl { get; set; }
}
