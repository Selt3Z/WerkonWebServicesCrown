using System.Diagnostics;
using Microsoft.Extensions.Options;
using Crown.Contracts.Common;

namespace Crown.Infrastructure.Backups;

public sealed class ManualBackupService
{
    private static int _inProgress;

    private readonly BackupOptions _options;
    private readonly ILogger<ManualBackupService> _logger;

    public ManualBackupService(
        IOptions<BackupOptions> options,
        ILogger<ManualBackupService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<(bool Success, string? ErrorCode, string Message)> RunDumpAsync(
        CancellationToken cancellationToken)
        => await RunAsync("--dump-once", "Manual dump backup completed.", cancellationToken);

    public async Task<(bool Success, string? ErrorCode, string Message)> RunResticAsync(
        CancellationToken cancellationToken)
        => await RunAsync("--restic-once", "Manual restic backup completed.", cancellationToken);

    private async Task<(bool Success, string? ErrorCode, string Message)> RunAsync(
        string modeArg,
        string successMessage,
        CancellationToken cancellationToken)
    {
        if (Interlocked.CompareExchange(ref _inProgress, 1, 0) != 0)
        {
            return (false, ApiErrorCodes.BackupAlreadyRunning, "A backup is already running.");
        }

        try
        {
            var container = string.IsNullOrWhiteSpace(_options.ContainerName)
                ? "crown_backup"
                : _options.ContainerName.Trim();
            var docker = string.IsNullOrWhiteSpace(_options.DockerBinary)
                ? "docker"
                : _options.DockerBinary.Trim();

            var psi = new ProcessStartInfo
            {
                FileName = docker,
                ArgumentList = { "exec", container, "/usr/local/bin/backup.sh", modeArg },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start docker exec for backup.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 2)
            {
                return (false, ApiErrorCodes.BackupAlreadyRunning, "A backup is already running.");
            }

            if (process.ExitCode != 0)
            {
                _logger.LogError(
                    "Manual backup failed. Mode={Mode} Exit={Exit} Stderr={Stderr} Stdout={Stdout}",
                    modeArg, process.ExitCode, stderr, stdout);
                var detail = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
                detail = TruncateForClient(detail);
                return (
                    false,
                    ApiErrorCodes.BackupFailed,
                    string.IsNullOrWhiteSpace(detail)
                        ? $"Backup failed with exit code {process.ExitCode}."
                        : detail);
            }

            _logger.LogInformation("Manual backup finished. Mode={Mode}", modeArg);
            return (true, null, successMessage);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Manual backup threw. Mode={Mode}", modeArg);
            return (false, ApiErrorCodes.BackupFailed, TruncateForClient(ex.Message));
        }
        finally
        {
            Interlocked.Exchange(ref _inProgress, 0);
        }
    }

    private static string TruncateForClient(string? value, int max = 240)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
