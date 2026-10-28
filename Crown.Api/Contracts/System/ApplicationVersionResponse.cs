namespace Crown.Contracts.System;

public sealed class ApplicationVersionResponse
{
    public string Version { get; set; } = string.Empty;

    public string ShortVersion { get; set; } = string.Empty;
}
