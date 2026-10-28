namespace Crown.Web.Models;

// DTO зеркалят контракты Crown.Api (без project reference — Web остаётся тонким UI).

public static class AppRoles
{
    public const string Administrator = "Administrator";
    public const string Dispatcher = "Dispatcher";
    public const string Initiator = "Initiator";

    /// <summary>Админ или диспетчер — ведут заявки, склад, каталог, журнал.</summary>
    public const string Staff = Administrator + "," + Dispatcher;

    public static readonly string[] All = [Administrator, Dispatcher, Initiator];

    public static string Display(string? role) => role switch
    {
        Administrator => "Администратор",
        Dispatcher => "Диспетчер",
        Initiator => "Инициатор",
        _ => role ?? string.Empty
    };
}

public sealed class PagedResponse<T>
{
    public List<T> Items { get; set; } = [];
    public bool HasMore { get; set; }
}

public sealed class CurrentUserResponse
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = [];
}

public sealed class LoginRequest
{
    public string UserName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public sealed class LoginFailureResponse
{
    public string Code { get; set; } = string.Empty;
    public DateTimeOffset? LockoutEndUtc { get; set; }
}

public enum ServiceRequestStatus
{
    Submitted = 1,
    InProgress = 2,
    Rejected = 3,
    Returned = 4,
    Completed = 5,
    Cancelled = 6
}

public sealed class ServiceRequestListItem
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

public sealed class ServiceRequestDetail
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
    public List<RequestAttachment> Attachments { get; set; } = [];
}

public sealed class RequestAttachment
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset UploadedAt { get; set; }
}

public sealed class ServiceDepartment
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

public sealed class WarehousePart
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Barcode { get; set; }
    public string? Category { get; set; }
    public decimal DefaultUnitPrice { get; set; }
    public string DefaultUnit { get; set; } = string.Empty;
    public decimal QuantityOnHand { get; set; }
    public decimal MinQuantityOnHand { get; set; }
    public string? Description { get; set; }
    public bool IsArchived { get; set; }
}

public sealed class CatalogPart
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Code { get; set; }
    public string? Category { get; set; }
    public decimal DefaultUnitPrice { get; set; }
    public string DefaultUnit { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
}

public sealed class AuditLogItem
{
    public Guid Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string UserDisplayName { get; set; } = string.Empty;
    public Guid? UserId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
}

public sealed class AuditLogUserOption
{
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class UserListItem
{
    public Guid Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? LastActivityAtUtc { get; set; }
}

public sealed class SaveUserRequest
{
    public string UserName { get; set; } = string.Empty;
    public string? Password { get; set; }
    public string? CurrentPassword { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public sealed class SettingsResponse
{
    public string TimeZoneId { get; set; } = string.Empty;
    public string Theme { get; set; } = "light";
    public string CurrencyCode { get; set; } = "RUB";
    public int CurrencyDecimalPlaces { get; set; } = 2;
    public bool HideArchivedByDefault { get; set; } = true;
    public bool DispatcherCanHardDelete { get; set; }
    public bool WarehouseEnabled { get; set; }
    public int AuditRetentionDays { get; set; } = 30;
    public int ListPageSize { get; set; } = 30;
}

public sealed class UpdateSettingsRequest
{
    public string? TimeZoneId { get; set; }
    public string? Theme { get; set; }
    public string? CurrencyCode { get; set; }
    public bool? HideArchivedByDefault { get; set; }
    public bool? DispatcherCanHardDelete { get; set; }
    public bool? WarehouseEnabled { get; set; }
    public int? AuditRetentionDays { get; set; }
    public int? ListPageSize { get; set; }
}

public sealed class UpdateTimeZoneRequest
{
    public string TimeZoneId { get; set; } = string.Empty;
}

public sealed class BackupStatus
{
    public bool IsConfigured { get; set; }
    public string? LastRunUtc { get; set; }
    public string? LastStatus { get; set; }
    public string? LastMessage { get; set; }
    public long? LastBackupSizeBytes { get; set; }
    public bool ResticEnabled { get; set; }
    public bool BackupOkToday { get; set; }
    public bool IsRunning { get; set; }
    public string? DailyAt { get; set; }
}

public sealed class DiskVolume
{
    public string Name { get; set; } = string.Empty;
    public string Mount { get; set; } = string.Empty;
    public long TotalBytes { get; set; }
    public long UsedBytes { get; set; }
    public long FreeBytes { get; set; }
    public int UsedPercent { get; set; }
}

public sealed class ComposeServiceStatus
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Health { get; set; }
}

public sealed class SystemHealthDetails
{
    public List<string> PendingMigrations { get; set; } = [];
    public List<DiskVolume> Disks { get; set; } = [];
    public List<ComposeServiceStatus> Containers { get; set; } = [];
}

public sealed class SystemStatusResponse
{
    public string ApplicationVersion { get; set; } = string.Empty;
    public bool DatabaseHealthy { get; set; }
    public bool DatabaseSchemaUpToDate { get; set; }
    public int PendingMigrationCount { get; set; }
    public string? LastAppliedMigration { get; set; }
    public string ServerTimeUtc { get; set; } = string.Empty;
    public string AppTimeZoneId { get; set; } = string.Empty;
    public string ServerLocalTime { get; set; } = string.Empty;
    public int AuditLogEntryCount { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string PublicHostname { get; set; } = string.Empty;
    public List<string> AdvertiseAddresses { get; set; } = [];
    public BackupStatus Backup { get; set; } = new();
    public SystemHealthDetails Health { get; set; } = new();
}

public sealed class ManualBackupResponse
{
    public bool Success { get; set; }
    public string? ErrorCode { get; set; }
    public string? Message { get; set; }
}
