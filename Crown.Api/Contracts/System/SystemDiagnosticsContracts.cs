namespace Crown.Contracts.System;

public sealed class DiskVolumeResponse
{
    public string Name { get; set; } = string.Empty;

    public string Mount { get; set; } = string.Empty;

    public long TotalBytes { get; set; }

    public long UsedBytes { get; set; }

    public long FreeBytes { get; set; }

    public int UsedPercent { get; set; }
}

public sealed class ComposeServiceStatusResponse
{
    public string Name { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? Health { get; set; }
}

public sealed class SystemHealthDetailsResponse
{
    public bool DatabaseHealthy { get; set; }

    public bool DatabaseSchemaUpToDate { get; set; }

    public int PendingMigrationCount { get; set; }

    public List<string> PendingMigrations { get; set; } = [];

    public string? LastAppliedMigration { get; set; }

    public bool BackupOkToday { get; set; }

    public string? BackupLastStatus { get; set; }

    public string? BackupLastRunUtc { get; set; }

    public List<DiskVolumeResponse> Disks { get; set; } = [];

    public List<ComposeServiceStatusResponse> Containers { get; set; } = [];
}

public sealed class SupportContactResponse
{
    public bool Sent { get; set; }

    public bool WebhookConfigured { get; set; }

    public string ReportText { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string? Message { get; set; }
}

public sealed class ResetSessionsResponse
{
    public bool Success { get; set; }

    public int UsersAffected { get; set; }

    public string? Message { get; set; }
}
