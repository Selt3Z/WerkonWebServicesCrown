namespace Crown.Infrastructure.Backups;

public sealed class BackupFileException : Exception
{
    public string ErrorCode { get; }

    public BackupFileException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }
}
