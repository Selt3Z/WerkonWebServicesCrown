namespace Crown.Contracts.System;

public sealed class NetplanFileResponse
{
    public bool Enabled { get; set; }

    public string Path { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? ErrorCode { get; set; }

    public string? Message { get; set; }
}

public sealed class SaveNetplanRequest
{
    public string? Content { get; set; }

    /// <summary>Должно быть NETPLAN.</summary>
    public string? Confirmation { get; set; }

    /// <summary>После записи вызвать netplan apply.</summary>
    public bool Apply { get; set; } = true;
}

public sealed class SaveNetplanResponse
{
    public bool Saved { get; set; }

    public bool Applied { get; set; }

    public string? ErrorCode { get; set; }

    public string? Message { get; set; }
}
