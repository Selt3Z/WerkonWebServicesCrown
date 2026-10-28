namespace Crown.Contracts.Settings;

public sealed class BackupStatusResponse
{
    public bool IsConfigured { get; set; }

    public string? LastRunUtc { get; set; }

    public string? LastStatus { get; set; }

    public string? LastMessage { get; set; }

    public long? LastBackupSizeBytes { get; set; }

    public bool ResticEnabled { get; set; }

    public bool BackupOkToday { get; set; }

    /// <summary>Сейчас идёт dump/restic (из status.json).</summary>
    public bool IsRunning { get; set; }

    /// <summary>0–100; обновляется скриптом бэкапа, UI может читать при refresh.</summary>
    public int ProgressPercent { get; set; }

    /// <summary>Интервал автобэкапа (сек), fallback если нет DailyAt.</summary>
    public int IntervalSeconds { get; set; }

    /// <summary>Локальное время ежедневного автобэкапа (HH:mm), пусто если interval-режим.</summary>
    public string? DailyAt { get; set; }

    public string? TimeZoneId { get; set; }

    public int DailyRetentionDays { get; set; }

    public int WeeklyRetentionWeeks { get; set; }
}
