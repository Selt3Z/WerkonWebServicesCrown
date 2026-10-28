using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.System;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Backups;
using Crown.Infrastructure.Diagnostics;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;
using Crown.Infrastructure.Time;
using Crown.Infrastructure.HostOps;

namespace Crown.Features.System;

[ApiController]
[Route("api/system")]
[Authorize(Policy = AuthorizationPolicies.ManageUsers)]
[EnableRateLimiting("system-ops")]
public sealed class SystemStatusController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly AppSettingsService _appSettingsService;
    private readonly AppTimeZone _appTimeZone;
    private readonly BackupStatusReader _backupStatusReader;
    private readonly BackupCatalogService _backupCatalog;
    private readonly DatabaseRestoreService _databaseRestore;
    private readonly ManualBackupService _manualBackup;
    private readonly NetworkInfoReader _networkInfoReader;
    private readonly HostPowerService _hostPower;
    private readonly NetplanHostService _netplan;
    private readonly SystemDiagnosticsService _diagnostics;

    public SystemStatusController(
        AppDbContext dbContext,
        AppSettingsService appSettingsService,
        AppTimeZone appTimeZone,
        BackupStatusReader backupStatusReader,
        BackupCatalogService backupCatalog,
        DatabaseRestoreService databaseRestore,
        ManualBackupService manualBackup,
        NetworkInfoReader networkInfoReader,
        HostPowerService hostPower,
        NetplanHostService netplan,
        SystemDiagnosticsService diagnostics)
    {
        _dbContext = dbContext;
        _appSettingsService = appSettingsService;
        _appTimeZone = appTimeZone;
        _backupStatusReader = backupStatusReader;
        _backupCatalog = backupCatalog;
        _databaseRestore = databaseRestore;
        _manualBackup = manualBackup;
        _networkInfoReader = networkInfoReader;
        _hostPower = hostPower;
        _netplan = netplan;
        _diagnostics = diagnostics;
    }

    [HttpGet("status")]
    public async Task<ActionResult<SystemStatusResponse>> GetStatus(CancellationToken cancellationToken)
    {
        var databaseHealthy = await _dbContext.Database.CanConnectAsync(cancellationToken);
        var pendingMigrations = databaseHealthy
            ? (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList()
            : [];
        var appliedMigrations = databaseHealthy
            ? await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)
            : [];
        var auditCount = databaseHealthy
            ? await _dbContext.AuditLogEntries.CountAsync(cancellationToken)
            : 0;
        var timeZoneId = await _appSettingsService.GetTimeZoneIdAsync(cancellationToken);
        var serverUtc = DateTime.UtcNow;
        var backup = await _backupStatusReader.ReadAsync(cancellationToken);
        var network = _networkInfoReader.Read();
        var health = await _diagnostics.GetHealthDetailsAsync(cancellationToken);

        return new SystemStatusResponse
        {
            ApplicationVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
                ?? "1.0.1.1",
            DatabaseHealthy = databaseHealthy,
            DatabaseSchemaUpToDate = databaseHealthy && pendingMigrations.Count == 0,
            PendingMigrationCount = pendingMigrations.Count,
            LastAppliedMigration = appliedMigrations.LastOrDefault(),
            ServerTimeUtc = serverUtc.ToString("O"),
            AppTimeZoneId = timeZoneId,
            ServerLocalTime = _appTimeZone.FromUtc(serverUtc).ToString("yyyy-MM-dd HH:mm:ss"),
            AuditLogEntryCount = auditCount,
            MachineName = network.MachineName,
            PublicHostname = network.PublicHostname,
            AdvertiseAddresses = network.AdvertiseAddresses,
            InterfaceAddresses = network.InterfaceAddresses,
            Backup = backup,
            AvailableBackups = _backupCatalog.ListAvailable().ToList(),
            Health = health
        };
    }

    [HttpPost("backup/run-dump")]
    public async Task<ActionResult<ManualBackupResponse>> RunDumpBackup(CancellationToken cancellationToken)
    {
        var (success, errorCode, message) = await _manualBackup.RunDumpAsync(cancellationToken);
        var body = new ManualBackupResponse
        {
            Success = success,
            ErrorCode = errorCode,
            Message = message
        };

        return success ? Ok(body) : BadRequest(body);
    }

    [HttpPost("backup/run-restic")]
    public async Task<ActionResult<ManualBackupResponse>> RunResticBackup(CancellationToken cancellationToken)
    {
        var backup = await _backupStatusReader.ReadAsync(cancellationToken);
        if (!backup.ResticEnabled)
        {
            return BadRequest(new ManualBackupResponse
            {
                Success = false,
                ErrorCode = ApiErrorCodes.BackupResticDisabled,
                Message = "Restic is not enabled on this server."
            });
        }

        var (success, errorCode, message) = await _manualBackup.RunResticAsync(cancellationToken);
        var body = new ManualBackupResponse
        {
            Success = success,
            ErrorCode = errorCode,
            Message = message
        };

        return success ? Ok(body) : BadRequest(body);
    }

    [HttpPost("restore-backup")]
    public async Task<IActionResult> RestoreBackup(
        [FromBody] RestoreBackupRequest request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Confirmation?.Trim(), "RESTORE", StringComparison.Ordinal))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.RestoreConfirmRequired));
        }

        if (!_backupCatalog.TryResolveSafePath(request.RelativePath, out var fullPath))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.BackupFileNotFound));
        }

        try
        {
            await _databaseRestore.RestoreAsync(fullPath, cancellationToken);
        }
        catch (BackupFileException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.ErrorCode));
        }

        return Ok(new { message = "Database restored. Sign in again if the session was dropped." });
    }

    [HttpGet("backup/download")]
    public IActionResult DownloadBackup([FromQuery] string path)
    {
        if (!_backupCatalog.TryResolveSafePath(path, out var fullPath))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.BackupFileNotFound));
        }

        var fileName = Path.GetFileName(fullPath);
        return PhysicalFile(fullPath, "application/gzip", fileName);
    }

    [HttpPost("backup/upload")]
    [RequestSizeLimit(512 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 512 * 1024 * 1024)]
    public async Task<ActionResult<BackupFileInfoResponse>> UploadBackup(
        IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.BackupFileNotFound));
        }

        if (file.Length > BackupCatalogService.MaxCompressedBytes)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.BackupFileTooLarge));
        }

        var name = file.FileName ?? string.Empty;
        try
        {
            await using var stream = file.OpenReadStream();
            var saved = await _backupCatalog.SaveUploadedAsync(stream, name, cancellationToken);
            return Ok(saved);
        }
        catch (BackupFileException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.ErrorCode));
        }
    }

    [HttpPost("power/restart-stack")]
    public ActionResult<HostPowerResponse> RestartStack([FromBody] HostPowerRequest request)
    {
        var result = _hostPower.RestartStack(request.Confirmation);
        return string.IsNullOrWhiteSpace(result.ErrorCode) ? Accepted(result) : BadRequest(result);
    }

    [HttpPost("power/reboot-host")]
    public ActionResult<HostPowerResponse> RebootHost([FromBody] HostPowerRequest request)
    {
        var result = _hostPower.RebootHost(request.Confirmation);
        return string.IsNullOrWhiteSpace(result.ErrorCode) ? Accepted(result) : BadRequest(result);
    }

    [HttpPost("power/shutdown-host")]
    public ActionResult<HostPowerResponse> ShutdownHost([FromBody] HostPowerRequest request)
    {
        var result = _hostPower.ShutdownHost(request.Confirmation);
        return string.IsNullOrWhiteSpace(result.ErrorCode) ? Accepted(result) : BadRequest(result);
    }

    [HttpGet("netplan")]
    public async Task<ActionResult<NetplanFileResponse>> GetNetplan(CancellationToken cancellationToken)
    {
        var result = await _netplan.ReadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(result.ErrorCode) && result.ErrorCode != ApiErrorCodes.NetplanDisabled)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPut("netplan")]
    public async Task<ActionResult<SaveNetplanResponse>> SaveNetplan(
        [FromBody] SaveNetplanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _netplan.SaveAsync(request, cancellationToken);
        return string.IsNullOrWhiteSpace(result.ErrorCode) ? Ok(result) : BadRequest(result);
    }

    [HttpGet("logs/{service}")]
    public async Task<IActionResult> DownloadLogs(
        string service,
        [FromQuery] int mb = 2,
        CancellationToken cancellationToken = default)
    {
        var logs = await _diagnostics.GetContainerLogsAsync(service, mb, cancellationToken);
        if (logs is null)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.LogServiceInvalid));
        }

        return File(
            Utf8TextFile.EnsureBom(logs.Value.Content),
            "text/plain; charset=utf-8",
            logs.Value.FileName);
    }

    [HttpPost("sessions/reset-all")]
    public async Task<ActionResult<ResetSessionsResponse>> ResetAllSessions(
        [FromBody] ConfirmationRequest? request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request?.Confirmation?.Trim(), "SESSIONS", StringComparison.Ordinal))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.SessionsConfirmRequired));
        }

        var result = await _diagnostics.ResetAllSessionsAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPost("support/contact")]
    public async Task<ActionResult<SupportContactResponse>> ContactDeveloper(CancellationToken cancellationToken)
    {
        var result = await _diagnostics.ContactDeveloperAsync(cancellationToken);
        return Ok(result);
    }
}
