namespace Crown.Contracts.System;

public sealed class ManualBackupResponse
{
    public bool Success { get; set; }

    public string? ErrorCode { get; set; }

    public string? Message { get; set; }
}
