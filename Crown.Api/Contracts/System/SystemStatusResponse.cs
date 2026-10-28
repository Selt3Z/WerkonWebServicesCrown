using Crown.Contracts.Settings;

namespace Crown.Contracts.System;

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

    public List<string> InterfaceAddresses { get; set; } = [];

    public BackupStatusResponse Backup { get; set; } = new();

    public List<BackupFileInfoResponse> AvailableBackups { get; set; } = [];

    public SystemHealthDetailsResponse Health { get; set; } = new();
}
