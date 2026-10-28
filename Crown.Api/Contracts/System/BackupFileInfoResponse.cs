namespace Crown.Contracts.System;

public sealed class BackupFileInfoResponse
{
    public string RelativePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string Folder { get; set; } = string.Empty;

    public long SizeBytes { get; set; }

    public string CreatedUtc { get; set; } = string.Empty;
}

public sealed class RestoreBackupRequest
{
    public string RelativePath { get; set; } = string.Empty;

    public string Confirmation { get; set; } = string.Empty;
}

public sealed class ConfirmationRequest
{
    public string Confirmation { get; set; } = string.Empty;
}
