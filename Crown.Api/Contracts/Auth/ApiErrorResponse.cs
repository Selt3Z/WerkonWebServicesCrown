namespace Crown.Contracts.Auth;

public sealed class ApiErrorResponse
{
    public string? Code { get; set; }

    public List<string> Codes { get; set; } = [];

    public string Message { get; set; } = string.Empty;
}
