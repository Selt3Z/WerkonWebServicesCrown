using System.Diagnostics;
using Microsoft.Extensions.Options;
using Crown.Contracts.Common;
using Crown.Contracts.System;

namespace Crown.Infrastructure.HostOps;

public sealed class HostPowerService
{
    private static int _actionInProgress;

    private readonly HostOpsOptions _options;
    private readonly ILogger<HostPowerService> _logger;

    public HostPowerService(
        IOptions<HostOpsOptions> options,
        ILogger<HostPowerService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public HostPowerResponse RestartStack(string? confirmation)
        => StartDetached(
            expectedConfirm: "RESTART",
            confirmation,
            BuildRestartStackScript,
            "Stack restart started. The site will be briefly unavailable.");

    public HostPowerResponse RebootHost(string? confirmation)
        => StartDetached(
            expectedConfirm: "REBOOT",
            confirmation,
            () => BuildHostShutdownScript(reboot: true),
            "Host reboot started.");

    public HostPowerResponse ShutdownHost(string? confirmation)
        => StartDetached(
            expectedConfirm: "SHUTDOWN",
            confirmation,
            () => BuildHostShutdownScript(reboot: false),
            "Host shutdown started.");

    private HostPowerResponse StartDetached(
        string expectedConfirm,
        string? confirmation,
        Func<string> buildScript,
        string startedMessage)
    {
        if (!_options.HostPowerEnabled)
        {
            return new HostPowerResponse
            {
                ErrorCode = ApiErrorCodes.HostPowerDisabled,
                Message = "Host power actions are disabled on this server."
            };
        }

        if (!string.Equals(confirmation?.Trim(), expectedConfirm, StringComparison.Ordinal))
        {
            return new HostPowerResponse
            {
                ErrorCode = ApiErrorCodes.HostPowerConfirmRequired,
                Message = $"Type {expectedConfirm} to confirm."
            };
        }

        if (Interlocked.CompareExchange(ref _actionInProgress, 1, 0) != 0)
        {
            return new HostPowerResponse
            {
                ErrorCode = ApiErrorCodes.HostPowerAlreadyRunning,
                Message = "A power action is already in progress."
            };
        }

        try
        {
            EnsureRepositoryReady();

            var scriptsDir = Path.Combine(_options.RepositoryPath, "scripts");
            Directory.CreateDirectory(scriptsDir);
            var scriptPath = Path.Combine(scriptsDir, $".power-{expectedConfirm.ToLowerInvariant()}.sh");
            File.WriteAllText(scriptPath, buildScript().Replace("\r\n", "\n", StringComparison.Ordinal));

            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                ArgumentList = { scriptPath },
                WorkingDirectory = _options.RepositoryPath,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start host power script.");

            _logger.LogWarning(
                "Host power action {Action} started, pid={Pid}",
                expectedConfirm,
                process.Id);
            process.Dispose();

            return new HostPowerResponse
            {
                Started = true,
                Message = startedMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Host power action {Action} failed", expectedConfirm);
            return new HostPowerResponse
            {
                ErrorCode = ApiErrorCodes.HostPowerFailed,
                Message = "Failed to start the power action."
            };
        }
        finally
        {
            // Detached-скрипт; гейт снимаем сразу, иначе после рестарта UI «залипнет».
            Interlocked.Exchange(ref _actionInProgress, 0);
        }
    }

    private string BuildRestartStackScript()
    {
        var repo = _options.RepositoryPath;
        var composePath = Path.Combine(repo, _options.ComposeFileName);
        return $"""
            #!/bin/bash
            set -euo pipefail
            sleep 2
            cd "{repo}"
            "{_options.DockerBinary}" compose -f "{composePath}" --project-directory "{repo}" restart api web
            """;
    }

    private string BuildHostShutdownScript(bool reboot)
    {
        // docker.sock + nsenter PID 1 (Linux VPS). На Docker Desktop — ребут Linux VM.
        var flag = reboot ? "-r" : "-h";
        var image = _options.HostPowerHelperImage.Replace("\"", string.Empty, StringComparison.Ordinal);
        return $"""
            #!/bin/bash
            set -euo pipefail
            sleep 2
            "{_options.DockerBinary}" run --rm --privileged --pid=host "{image}" \
              nsenter -t 1 -m -u -i -n shutdown {flag} now
            """;
    }

    private void EnsureRepositoryReady()
    {
        if (string.IsNullOrWhiteSpace(_options.RepositoryPath)
            || !Directory.Exists(_options.RepositoryPath))
        {
            throw new InvalidOperationException(
                $"Update repository path is missing: {_options.RepositoryPath}");
        }
    }
}
