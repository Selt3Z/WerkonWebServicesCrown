namespace Crown.Infrastructure.HostOps;

/// <summary>
/// Настройки операций с хостом (docker compose, power, netplan).
/// GitHub-обновление из UI удалено: обновление стека выполняется вне приложения.
/// </summary>
public sealed class HostOpsOptions
{
    public const string SectionName = "HostOps";

    /// <summary>Absolute path to the compose project checkout (mounted into the API container).</summary>
    public string RepositoryPath { get; set; } = "/update/repo";

    public string ComposeFileName { get; set; } = "docker-compose.yml";

    public string DockerBinary { get; set; } = "docker";

    public string ApiContainerName { get; set; } = "crown_api";

    public string WebContainerName { get; set; } = "crown_web";

    /// <summary>Разрешить reboot/shutdown хоста и restart стека из UI.</summary>
    public bool HostPowerEnabled { get; set; } = true;

    /// <summary>Образ для nsenter на PID 1 хоста (нужен docker.sock).</summary>
    public string HostPowerHelperImage { get; set; } = "alpine:3.20";

    /// <summary>Редактирование /etc/netplan/... с хоста через nsenter (Linux VPS).</summary>
    public bool NetplanEnabled { get; set; } = true;

    public string NetplanFilePath { get; set; } = "/etc/netplan/00-installer-config.yaml";
}
