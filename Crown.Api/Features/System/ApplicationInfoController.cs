using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Crown.Contracts.System;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Backups;
using Crown.Infrastructure.Identity;

namespace Crown.Features.System;

[ApiController]
[Route("api/system")]
public sealed class ApplicationInfoController : ControllerBase
{
    private readonly BackupStatusReader _backupStatusReader;

    public ApplicationInfoController(BackupStatusReader backupStatusReader)
    {
        _backupStatusReader = backupStatusReader;
    }

    [HttpGet("version")]
    [Authorize(Policy = AuthorizationPolicies.BusinessData)]
    public ActionResult<ApplicationVersionResponse> GetVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "1.0.1.1";

        return Ok(new ApplicationVersionResponse
        {
            Version = version,
            ShortVersion = FormatShortVersion(version)
        });
    }

    [HttpGet("admin-alerts")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<SystemAdminAlertsResponse>> GetAdminAlerts(CancellationToken cancellationToken)
    {
        var backup = await _backupStatusReader.ReadAsync(cancellationToken);

        // Только бэкап-сигналы из status.json — без схемы БД (клиенту бесполезно) и без docker/df.
        var backupFailed = string.Equals(backup.LastStatus, "error", StringComparison.OrdinalIgnoreCase);
        var backupStale = IsBackupStale(backup);
        var backupNotOkToday = !backup.BackupOkToday;

        return Ok(new SystemAdminAlertsResponse
        {
            HasBackupAlert = backupNotOkToday,
            HasBackupFailedAlert = backupFailed,
            HasBackupStaleAlert = backupStale,
            HasSchemaAlert = false,
            HasDiskAlert = false
        });
    }

    /// <summary>
    /// Нет LastRunUtc или последний запуск старше max(48ч, 2×IntervalSeconds).
    /// </summary>
    private static bool IsBackupStale(Contracts.Settings.BackupStatusResponse backup)
    {
        var intervalSeconds = backup.IntervalSeconds > 0 ? backup.IntervalSeconds : 86400;
        var threshold = TimeSpan.FromSeconds(Math.Max(intervalSeconds * 2.0, 48 * 3600));

        if (string.IsNullOrWhiteSpace(backup.LastRunUtc)
            || !DateTime.TryParse(backup.LastRunUtc, out var lastRun))
        {
            return true;
        }

        var lastUtc = DateTime.SpecifyKind(lastRun, DateTimeKind.Utc);
        return DateTime.UtcNow - lastUtc > threshold;
    }

    internal static string FormatShortVersion(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length >= 2)
        {
            return $"{parts[0]}.{parts[1]}";
        }

        return version;
    }
}
