using System.Text.Json;
using Microsoft.Extensions.Options;
using Crown.Contracts.Settings;
using Crown.Infrastructure.Time;

namespace Crown.Infrastructure.Backups;

public sealed class BackupStatusReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BackupOptions _options;
    private readonly AppTimeZone _appTimeZone;

    public BackupStatusReader(IOptions<BackupOptions> options, AppTimeZone appTimeZone)
    {
        _options = options.Value;
        _appTimeZone = appTimeZone;
    }

    public async Task<BackupStatusResponse> ReadAsync(CancellationToken cancellationToken = default)
    {
        var schedule = ApplySchedule(new BackupStatusResponse());

        if (string.IsNullOrWhiteSpace(_options.StatusFilePath) || !File.Exists(_options.StatusFilePath))
        {
            schedule.IsConfigured = false;
            schedule.LastMessage = "Backup status file is not available.";
            return schedule;
        }

        try
        {
            await using var stream = File.OpenRead(_options.StatusFilePath);
            var payload = await JsonSerializer.DeserializeAsync<BackupStatusFile>(stream, JsonOptions, cancellationToken);

            if (payload is null)
            {
                schedule.IsConfigured = true;
                schedule.LastStatus = "unknown";
                schedule.LastMessage = "Backup status file is empty.";
                return schedule;
            }

            var okToday = false;
            if (string.Equals(payload.LastStatus, "success", StringComparison.OrdinalIgnoreCase)
                && DateTime.TryParse(payload.LastRunUtc, out var lastRun))
            {
                var lastLocal = _appTimeZone.FromUtc(DateTime.SpecifyKind(lastRun, DateTimeKind.Utc));
                var today = _appTimeZone.GetToday();
                okToday = DateOnly.FromDateTime(lastLocal) == today;
            }

            var isRunning = payload.IsRunning
                || string.Equals(payload.LastStatus, "running", StringComparison.OrdinalIgnoreCase);

            // Залипший running после крэша/старого flock: через 30 мин считаем не running,
            // иначе UI вечно прячет кнопки и отвечает «уже выполняется».
            if (isRunning
                && DateTime.TryParse(payload.LastRunUtc, out var runningSinceUtc)
                && DateTime.UtcNow - DateTime.SpecifyKind(runningSinceUtc, DateTimeKind.Utc)
                    > TimeSpan.FromMinutes(30))
            {
                isRunning = false;
                if (string.Equals(payload.LastStatus, "running", StringComparison.OrdinalIgnoreCase))
                {
                    payload.LastStatus = "error";
                    payload.LastMessage = "Backup marked stale (was running > 30 min).";
                }
            }

            schedule.IsConfigured = true;
            schedule.LastRunUtc = payload.LastRunUtc;
            schedule.LastStatus = payload.LastStatus;
            schedule.LastMessage = payload.LastMessage;
            schedule.LastBackupSizeBytes = payload.LastBackupSizeBytes;
            schedule.ResticEnabled = payload.ResticEnabled || IsResticConfiguredInOptions();
            schedule.BackupOkToday = okToday;
            schedule.IsRunning = isRunning;
            schedule.ProgressPercent = Math.Clamp(payload.ProgressPercent, 0, 100);
            return schedule;
        }
        catch (Exception ex)
        {
            schedule.IsConfigured = true;
            schedule.LastStatus = "error";
            schedule.LastMessage = ex.Message;
            schedule.ResticEnabled = IsResticConfiguredInOptions();
            return schedule;
        }
    }

    private BackupStatusResponse ApplySchedule(BackupStatusResponse response)
    {
        response.IntervalSeconds = _options.IntervalSeconds > 0 ? _options.IntervalSeconds : 86400;
        response.DailyAt = string.IsNullOrWhiteSpace(_options.DailyAt) ? null : _options.DailyAt.Trim();
        response.TimeZoneId = string.IsNullOrWhiteSpace(_options.TimeZoneId)
            ? null
            : _options.TimeZoneId.Trim();
        response.DailyRetentionDays = _options.DailyRetentionDays;
        response.WeeklyRetentionWeeks = _options.WeeklyRetentionWeeks;
        response.ResticEnabled = IsResticConfiguredInOptions();
        return response;
    }

    private bool IsResticConfiguredInOptions() =>
        !string.IsNullOrWhiteSpace(_options.ResticRepository);

    private sealed class BackupStatusFile
    {
        public string? LastRunUtc { get; set; }

        public string? LastStatus { get; set; }

        public string? LastMessage { get; set; }

        public long? LastBackupSizeBytes { get; set; }

        public bool ResticEnabled { get; set; }

        public bool IsRunning { get; set; }

        public int ProgressPercent { get; set; }
    }
}
