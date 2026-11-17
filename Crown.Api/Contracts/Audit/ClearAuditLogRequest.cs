namespace Crown.Contracts.Audit;

public sealed class ClearAuditLogRequest
{
    /// <summary>Должно быть ровно «ОЧИСТИТЬ».</summary>
    public string? Confirmation { get; set; }

    /// <summary>Текущий пароль администратора.</summary>
    public string? Password { get; set; }
}

public sealed class ClearAuditLogResponse
{
    public int Deleted { get; set; }
}
