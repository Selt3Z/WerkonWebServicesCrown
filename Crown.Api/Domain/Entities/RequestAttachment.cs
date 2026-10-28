namespace Crown.Domain.Entities;

public sealed class RequestAttachment
{
    public Guid Id { get; set; }
    public long ServiceRequestId { get; set; }
    public ServiceRequest? ServiceRequest { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    /// <summary>Относительный путь внутри каталога вложений.</summary>
    public string StoragePath { get; set; } = string.Empty;
    public DateTimeOffset UploadedAt { get; set; }
    public Guid? UploadedByUserId { get; set; }
}
