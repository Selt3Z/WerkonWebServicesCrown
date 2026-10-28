using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using Crown.Contracts.Common;
using Crown.Contracts.System;

namespace Crown.Infrastructure.HostOps;

/// <summary>
/// Чтение/запись netplan на хосте через docker.sock + nsenter (как power).
/// </summary>
public sealed class NetplanHostService
{
    private const int MaxContentBytes = 64 * 1024;

    private readonly HostOpsOptions _options;
    private readonly ILogger<NetplanHostService> _logger;

    public NetplanHostService(
        IOptions<HostOpsOptions> options,
        ILogger<NetplanHostService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<NetplanFileResponse> ReadAsync(CancellationToken cancellationToken)
    {
        if (!_options.NetplanEnabled)
        {
            return DisabledResponse();
        }

        var path = NormalizePath(_options.NetplanFilePath);
        try
        {
            var (exit, stdout, stderr) = await RunNsenterAsync(
                ["sh", "-c", $"cat '{path}'"],
                stdin: null,
                cancellationToken);

            if (exit != 0)
            {
                _logger.LogWarning("Netplan read failed: {Err}", Truncate(stderr));
                return new NetplanFileResponse
                {
                    Enabled = true,
                    Path = path,
                    ErrorCode = ApiErrorCodes.NetplanReadFailed,
                    Message = "Failed to read netplan file from the host."
                };
            }

            return new NetplanFileResponse
            {
                Enabled = true,
                Path = path,
                Content = stdout
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Netplan read failed");
            return new NetplanFileResponse
            {
                Enabled = true,
                Path = path,
                ErrorCode = ApiErrorCodes.NetplanReadFailed,
                Message = "Failed to read netplan file from the host."
            };
        }
    }

    public async Task<SaveNetplanResponse> SaveAsync(
        SaveNetplanRequest request,
        CancellationToken cancellationToken)
    {
        if (!_options.NetplanEnabled)
        {
            return new SaveNetplanResponse
            {
                ErrorCode = ApiErrorCodes.NetplanDisabled,
                Message = "Netplan editing is disabled on this server."
            };
        }

        if (!string.Equals(request.Confirmation?.Trim(), "NETPLAN", StringComparison.Ordinal))
        {
            return new SaveNetplanResponse
            {
                ErrorCode = ApiErrorCodes.NetplanConfirmRequired,
                Message = "Type NETPLAN to confirm."
            };
        }

        var content = request.Content ?? string.Empty;
        if (content.IndexOf('\0') >= 0
            || Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
        {
            return new SaveNetplanResponse
            {
                ErrorCode = ApiErrorCodes.NetplanInvalid,
                Message = "Netplan content is empty, too large, or invalid."
            };
        }

        // Минимальная защита от мусора: файл должен выглядеть как netplan YAML.
        if (!content.Contains("network:", StringComparison.Ordinal))
        {
            return new SaveNetplanResponse
            {
                ErrorCode = ApiErrorCodes.NetplanInvalid,
                Message = "Netplan YAML must contain a top-level network: key."
            };
        }

        var path = NormalizePath(_options.NetplanFilePath);
        var bak = $"{path}.bak.{DateTime.UtcNow:yyyyMMddHHmmss}";

        try
        {
            // Бэкап → запись через tee (stdin) → опционально apply.
            var backup = await RunNsenterAsync(
                ["sh", "-c", $"cp '{path}' '{bak}' 2>/dev/null || true"],
                stdin: null,
                cancellationToken);

            if (backup.ExitCode != 0)
            {
                _logger.LogWarning("Netplan backup warning: {Err}", Truncate(backup.StdErr));
            }

            var writeScript =
                $"tee '{path}' >/dev/null && chmod 600 '{path}' 2>/dev/null || true";
            var write = await RunNsenterAsync(
                ["sh", "-c", writeScript],
                stdin: content.Replace("\r\n", "\n", StringComparison.Ordinal),
                cancellationToken);

            if (write.ExitCode != 0)
            {
                _logger.LogWarning("Netplan write failed: {Err}", Truncate(write.StdErr));
                return new SaveNetplanResponse
                {
                    ErrorCode = ApiErrorCodes.NetplanWriteFailed,
                    Message = "Failed to write netplan file on the host."
                };
            }

            if (!request.Apply)
            {
                return new SaveNetplanResponse
                {
                    Saved = true,
                    Applied = false,
                    Message = "Netplan file saved. Apply was skipped."
                };
            }

            var apply = await RunNsenterAsync(
                ["sh", "-c", "netplan generate && netplan apply"],
                stdin: null,
                cancellationToken);

            if (apply.ExitCode != 0)
            {
                _logger.LogError(
                    "Netplan apply failed after write. Backup at {Bak}. Err={Err}",
                    bak,
                    Truncate(apply.StdErr + "\n" + apply.StdOut));
                return new SaveNetplanResponse
                {
                    Saved = true,
                    Applied = false,
                    ErrorCode = ApiErrorCodes.NetplanApplyFailed,
                    Message = "File saved, but netplan apply failed. Check YAML or restore the .bak file on the host."
                };
            }

            return new SaveNetplanResponse
            {
                Saved = true,
                Applied = true,
                Message = "Netplan saved and applied."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Netplan save failed");
            return new SaveNetplanResponse
            {
                ErrorCode = ApiErrorCodes.NetplanWriteFailed,
                Message = "Failed to save netplan on the host."
            };
        }
    }

    private NetplanFileResponse DisabledResponse() => new()
    {
        Enabled = false,
        Path = NormalizePath(_options.NetplanFilePath),
        ErrorCode = ApiErrorCodes.NetplanDisabled,
        Message = "Netplan editing is disabled on this server."
    };

    private static string NormalizePath(string? path)
    {
        var value = string.IsNullOrWhiteSpace(path)
            ? "/etc/netplan/00-installer-config.yaml"
            : path.Trim();

        // Только абсолютный путь без traversal.
        if (!value.StartsWith("/etc/netplan/", StringComparison.Ordinal)
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains('\0'))
        {
            return "/etc/netplan/00-installer-config.yaml";
        }

        return value;
    }

    private async Task<(int ExitCode, string StdOut, string StdErr)> RunNsenterAsync(
        string[] hostCommand,
        string? stdin,
        CancellationToken cancellationToken)
    {
        var image = _options.HostPowerHelperImage.Replace("\"", string.Empty, StringComparison.Ordinal);
        var psi = new ProcessStartInfo
        {
            FileName = _options.DockerBinary,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--rm");
        psi.ArgumentList.Add("--privileged");
        psi.ArgumentList.Add("--pid=host");
        if (stdin is not null)
        {
            psi.ArgumentList.Add("-i");
        }

        psi.ArgumentList.Add(image);
        psi.ArgumentList.Add("nsenter");
        psi.ArgumentList.Add("-t");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-m");
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add("-n");
        foreach (var part in hostCommand)
        {
            psi.ArgumentList.Add(part);
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start docker nsenter.");

        if (stdin is not null)
        {
            await process.StandardInput.WriteAsync(stdin.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
            process.StandardInput.Close();
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string? Truncate(string? value, int max = 1000)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max] + "…";
    }
}
