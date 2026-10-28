using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Crown.Contracts.System;
using Crown.Infrastructure.Backups;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.HostOps;

namespace Crown.Infrastructure.Diagnostics;

public sealed class SystemDiagnosticsService
{
    private static readonly Regex SecretLike = new(
        @"(?i)(password|passwd|secret|token|api[_-]?key|authorization|connectionstring)\s*[=:]\s*\S+",
        RegexOptions.Compiled);

    private readonly HostOpsOptions _update;
    private readonly SupportOptions _support;
    private readonly BackupStatusReader _backupStatus;
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SystemDiagnosticsService> _logger;

    public SystemDiagnosticsService(
        IOptions<HostOpsOptions> update,
        IOptions<SupportOptions> support,
        BackupStatusReader backupStatus,
        AppDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        IHttpClientFactory httpClientFactory,
        ILogger<SystemDiagnosticsService> logger)
    {
        _update = update.Value;
        _support = support.Value;
        _backupStatus = backupStatus;
        _dbContext = dbContext;
        _userManager = userManager;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<SystemHealthDetailsResponse> GetHealthDetailsAsync(CancellationToken cancellationToken)
    {
        var databaseHealthy = await _dbContext.Database.CanConnectAsync(cancellationToken);
        var pending = databaseHealthy
            ? (await _dbContext.Database.GetPendingMigrationsAsync(cancellationToken)).ToList()
            : [];
        var applied = databaseHealthy
            ? await _dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)
            : [];
        var backup = await _backupStatus.ReadAsync(cancellationToken);

        return new SystemHealthDetailsResponse
        {
            DatabaseHealthy = databaseHealthy,
            DatabaseSchemaUpToDate = databaseHealthy && pending.Count == 0,
            PendingMigrationCount = pending.Count,
            PendingMigrations = pending,
            LastAppliedMigration = applied.LastOrDefault(),
            BackupOkToday = backup.BackupOkToday,
            BackupLastStatus = backup.LastStatus,
            BackupLastRunUtc = backup.LastRunUtc,
            Disks = await ReadDisksAsync(cancellationToken),
            Containers = await ReadComposePsAsync(cancellationToken)
        };
    }

    public async Task<(byte[] Content, string FileName)?> GetContainerLogsAsync(
        string service,
        int maxMegabytes,
        CancellationToken cancellationToken)
    {
        var container = ResolveContainerName(service);
        if (container is null)
        {
            return null;
        }

        var mb = Math.Clamp(maxMegabytes, 1, 10);
        var maxBytes = mb * 1024 * 1024;

        // --tail большой, режем по байтам после; stderr тоже в stdout.
        var (exit, stdout, stderr) = await RunDockerAsync(
            ["logs", "--tail", "8000", container],
            cancellationToken);

        var raw = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout + "\n" + stderr;
        var redacted = Redact(raw);
        var bytes = Encoding.UTF8.GetBytes(redacted);
        if (bytes.Length > maxBytes)
        {
            bytes = bytes[^maxBytes..];
        }

        return (bytes, $"{container}-logs.txt");
    }

    public async Task<ResetSessionsResponse> ResetAllSessionsAsync(CancellationToken cancellationToken)
    {
        var users = await _userManager.Users.ToListAsync(cancellationToken);
        var affected = 0;
        foreach (var user in users)
        {
            var result = await _userManager.UpdateSecurityStampAsync(user);
            if (result.Succeeded)
            {
                affected++;
            }
        }

        return new ResetSessionsResponse
        {
            Success = true,
            UsersAffected = affected,
            Message = "All sessions invalidated. Users must sign in again."
        };
    }

    public async Task<SupportContactResponse> ContactDeveloperAsync(CancellationToken cancellationToken)
    {
        var health = await GetHealthDetailsAsync(cancellationToken);
        var version = typeof(SystemDiagnosticsService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(SystemDiagnosticsService).Assembly.GetName().Version?.ToString()
            ?? "?";
        var report = await BuildReportAsync(version, health, cancellationToken);

        var webhook = _support.WebhookUrl?.Trim();
        var configured = !string.IsNullOrWhiteSpace(webhook);
        if (!configured)
        {
            return new SupportContactResponse
            {
                Sent = false,
                WebhookConfigured = false,
                ReportText = report,
                Message = "Webhook is not configured. Download/copy the report and send it to the developer manually."
            };
        }

        try
        {
            var client = _httpClientFactory.CreateClient(nameof(SystemDiagnosticsService));
            client.Timeout = TimeSpan.FromSeconds(20);

            HttpResponseMessage response;
            if (webhook!.Contains("api.telegram.org", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(_support.TelegramChatId))
            {
                // Telegram Bot API sendMessage — текст режем до лимита ~4096.
                var text = report.Length <= 3900 ? report : report[..3900] + "\n…(truncated)";
                response = await client.PostAsJsonAsync(
                    webhook,
                    new { chat_id = _support.TelegramChatId.Trim(), text },
                    cancellationToken);
            }
            else
            {
                response = await client.PostAsJsonAsync(
                    webhook,
                    new
                    {
                        text = report,
                        version,
                        hostname = Environment.MachineName,
                        report
                    },
                    cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Support webhook failed: {Status} {Body}", (int)response.StatusCode, Truncate(body));
                return new SupportContactResponse
                {
                    Sent = false,
                    WebhookConfigured = true,
                    ReportText = report,
                    ErrorCode = "support_webhook_failed",
                    Message = $"Webhook returned HTTP {(int)response.StatusCode}."
                };
            }

            return new SupportContactResponse
            {
                Sent = true,
                WebhookConfigured = true,
                ReportText = report,
                Message = "Diagnostic report sent to the developer webhook."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Support webhook error");
            return new SupportContactResponse
            {
                Sent = false,
                WebhookConfigured = true,
                ReportText = report,
                ErrorCode = "support_webhook_failed",
                Message = "Failed to reach the developer webhook."
            };
        }
    }

    private async Task<string> BuildReportAsync(
        string version,
        SystemHealthDetailsResponse health,
        CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        sb.AppendLine("WWS Crown diagnostic report");
        sb.AppendLine($"Utc: {DateTime.UtcNow:O}");
        sb.AppendLine($"Version: {version}");
        sb.AppendLine($"Host: {Environment.MachineName}");
        sb.AppendLine($"DB healthy: {health.DatabaseHealthy}");
        sb.AppendLine($"Schema OK: {health.DatabaseSchemaUpToDate}");
        sb.AppendLine($"Pending migrations: {health.PendingMigrationCount}");
        if (health.PendingMigrations.Count > 0)
        {
            sb.AppendLine("  " + string.Join(", ", health.PendingMigrations.Take(20)));
        }

        sb.AppendLine($"Backup OK today: {health.BackupOkToday} ({health.BackupLastStatus ?? "—"})");
        sb.AppendLine("Disks:");
        foreach (var d in health.Disks.Take(12))
        {
            sb.AppendLine(
                $"  {d.Name} {d.Mount} free={FormatGb(d.FreeBytes)} / total={FormatGb(d.TotalBytes)} ({d.UsedPercent}% used)");
        }

        sb.AppendLine("Containers:");
        foreach (var c in health.Containers.Take(20))
        {
            sb.AppendLine($"  {c.Name}: {c.State} {c.Status} health={c.Health ?? "—"}");
        }

        // Короткие хвосты логов без секретов.
        foreach (var service in new[] { "api", "web", "backup" })
        {
            try
            {
                var logs = await GetContainerLogsAsync(service, 1, cancellationToken);
                if (logs is null)
                {
                    continue;
                }

                var text = Encoding.UTF8.GetString(logs.Value.Content);
                var tail = text.Length <= 1500 ? text : text[^1500..];
                sb.AppendLine($"--- last errors/log ({service}) ---");
                sb.AppendLine(tail.Trim());
            }
            catch
            {
                // ignore per-service log failures
            }
        }

        return sb.ToString();
    }

    private async Task<List<DiskVolumeResponse>> ReadDisksAsync(CancellationToken cancellationToken)
    {
        // Хостовые диски через nsenter; fallback — тома внутри контейнера.
        try
        {
            var (exit, stdout, _) = await RunNsenterAsync(
                ["sh", "-c", "df -B1 -P -x tmpfs -x devtmpfs -x overlay -x squashfs 2>/dev/null | tail -n +2"],
                cancellationToken);
            if (exit == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var list = new List<DiskVolumeResponse>();
                foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var parts = Regex.Split(line, @"\s+");
                    if (parts.Length < 6)
                    {
                        continue;
                    }

                    if (!long.TryParse(parts[1], out var total)
                        || !long.TryParse(parts[2], out var used)
                        || !long.TryParse(parts[3], out var free))
                    {
                        continue;
                    }

                    // % строго от тех же чисел, что в UI: (Всего − Свободно) / Всего.
                    // df.Used и Available часто расходятся (reserved/sparse) → used/total давал 0% при ~5% занятых.
                    var occupied = Math.Max(0L, total - free);
                    var pct = total <= 0
                        ? 0
                        : (int)Math.Clamp(Math.Round(100.0 * occupied / total), 0, 100);
                    list.Add(new DiskVolumeResponse
                    {
                        Name = parts[0],
                        Mount = parts[5],
                        TotalBytes = total,
                        UsedBytes = occupied,
                        FreeBytes = free,
                        UsedPercent = pct
                    });
                }

                if (list.Count > 0)
                {
                    return list;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Host df via nsenter failed, falling back to DriveInfo");
        }

        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d =>
            {
                var total = d.TotalSize;
                var free = d.AvailableFreeSpace;
                var occupied = Math.Max(0L, total - free);
                var pct = total <= 0
                    ? 0
                    : (int)Math.Clamp(Math.Round(100.0 * occupied / total), 0, 100);
                return new DiskVolumeResponse
                {
                    Name = d.Name,
                    Mount = d.RootDirectory.FullName,
                    TotalBytes = total,
                    UsedBytes = occupied,
                    FreeBytes = free,
                    UsedPercent = pct
                };
            })
            .ToList();
    }

    private async Task<List<ComposeServiceStatusResponse>> ReadComposePsAsync(CancellationToken cancellationToken)
    {
        var composePath = Path.Combine(_update.RepositoryPath, _update.ComposeFileName);
        if (!File.Exists(composePath))
        {
            return await ReadDockerPsFallbackAsync(cancellationToken);
        }

        var (exit, stdout, _) = await RunDockerAsync(
            [
                "compose", "-f", composePath, "--project-directory", _update.RepositoryPath,
                "ps", "--format", "json"
            ],
            cancellationToken);

        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return await ReadDockerPsFallbackAsync(cancellationToken);
        }

        var list = new List<ComposeServiceStatusResponse>();
        // compose v2: либо JSONL, либо один JSON-массив.
        var trimmed = stdout.Trim();
        try
        {
            if (trimmed.StartsWith('['))
            {
                using var doc = JsonDocument.Parse(trimmed);
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    list.Add(ParseComposeElement(el));
                }
            }
            else
            {
                foreach (var line in trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    using var doc = JsonDocument.Parse(line);
                    list.Add(ParseComposeElement(doc.RootElement));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse compose ps json");
            return await ReadDockerPsFallbackAsync(cancellationToken);
        }

        return list;
    }

    private async Task<List<ComposeServiceStatusResponse>> ReadDockerPsFallbackAsync(CancellationToken cancellationToken)
    {
        var (exit, stdout, _) = await RunDockerAsync(
            [
                "ps", "-a", "--filter", "name=crown_",
                "--format", "{{.Names}}\t{{.Status}}\t{{.State}}"
            ],
            cancellationToken);

        if (exit != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return [];
        }

        return stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line =>
            {
                var p = line.Split('\t');
                return new ComposeServiceStatusResponse
                {
                    Name = p.ElementAtOrDefault(0) ?? "",
                    Status = p.ElementAtOrDefault(1) ?? "",
                    State = p.ElementAtOrDefault(2) ?? "",
                    Health = null
                };
            })
            .ToList();
    }

    private static ComposeServiceStatusResponse ParseComposeElement(JsonElement el)
    {
        string Get(params string[] names)
        {
            foreach (var n in names)
            {
                if (el.TryGetProperty(n, out var p) && p.ValueKind == JsonValueKind.String)
                {
                    return p.GetString() ?? "";
                }
            }

            return "";
        }

        return new ComposeServiceStatusResponse
        {
            Name = Get("Name", "Service", "name"),
            State = Get("State", "state"),
            Status = Get("Status", "status"),
            Health = string.IsNullOrWhiteSpace(Get("Health", "health")) ? null : Get("Health", "health")
        };
    }

    private string? ResolveContainerName(string service) => service.Trim().ToLowerInvariant() switch
    {
        "api" => string.IsNullOrWhiteSpace(_update.ApiContainerName) ? "crown_api" : _update.ApiContainerName,
        "web" => string.IsNullOrWhiteSpace(_update.WebContainerName) ? "crown_web" : _update.WebContainerName,
        "backup" => "crown_backup",
        "postgres" or "db" => "crown_postgres",
        "proxy" => "crown_proxy",
        _ => null
    };

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunDockerAsync(
        IReadOnlyList<string> args,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _update.DockerBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args)
        {
            psi.ArgumentList.Add(a);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunNsenterAsync(
        string[] hostCommand,
        CancellationToken cancellationToken)
    {
        var image = _update.HostPowerHelperImage.Replace("\"", string.Empty, StringComparison.Ordinal);
        var args = new List<string>
        {
            "run", "--rm", "--privileged", "--pid=host", image,
            "nsenter", "-t", "1", "-m", "-u", "-i", "-n"
        };
        args.AddRange(hostCommand);
        return await RunDockerAsync(args, cancellationToken);
    }

    private static string Redact(string input) => SecretLike.Replace(input, "$1=***");

    private static string FormatGb(long bytes) => $"{bytes / (1024.0 * 1024.0 * 1024.0):0.##} GB";

    private static string? Truncate(string? value, int max = 500)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var t = value.Trim();
        return t.Length <= max ? t : t[..max] + "…";
    }
}
