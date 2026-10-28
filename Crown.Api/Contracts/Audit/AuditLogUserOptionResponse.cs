namespace Crown.Contracts.Audit;

public sealed class AuditLogUserOptionResponse
{
    public Guid UserId { get; set; }

    public string DisplayName { get; set; } = string.Empty;
}
