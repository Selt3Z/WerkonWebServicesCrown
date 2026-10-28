namespace Crown.Contracts.System;

public sealed class HostPowerRequest
{
    /// <summary>Для reboot: REBOOT; shutdown: SHUTDOWN; restart stack: RESTART.</summary>
    public string? Confirmation { get; set; }
}

public sealed class HostPowerResponse
{
    public bool Started { get; set; }

    public string? ErrorCode { get; set; }

    public string? Message { get; set; }
}
