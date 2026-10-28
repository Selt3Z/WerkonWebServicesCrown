namespace Crown.Infrastructure.Backups;

public sealed class BackupOptions
{
    public const string SectionName = "Backup";

    public string? StatusFilePath { get; set; }

    public string? BackupsDirectory { get; set; }

    /// <summary>Интервал автобэкапа в секундах (fallback, если DailyAt пустой).</summary>
    public int IntervalSeconds { get; set; } = 86400;

    /// <summary>Локальное время ежедневного бэкапа HH:mm (BACKUP_DAILY_AT).</summary>
    public string DailyAt { get; set; } = "03:00";

    /// <summary>Часовой пояс расписания (обычно AppTimeZone).</summary>
    public string? TimeZoneId { get; set; }

    public int DailyRetentionDays { get; set; } = 7;

    public int WeeklyRetentionWeeks { get; set; } = 4;

    public string ContainerName { get; set; } = "crown_backup";

    public string DockerBinary { get; set; } = "docker";

    /// <summary>Если задан (как RESTIC_REPOSITORY в .env) — restic считается включённым сразу.</summary>
    public string? ResticRepository { get; set; }
}
