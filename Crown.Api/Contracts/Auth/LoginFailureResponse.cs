namespace Crown.Contracts.Auth;

public static class LoginFailureCodes
{
    public const string InvalidCredentials = "invalid_credentials";
    public const string AccountDisabled = "account_disabled";
    public const string AccountLockedOut = "account_locked_out";
}

public sealed class LoginFailureResponse
{
    public string Code { get; set; } = LoginFailureCodes.InvalidCredentials;

    public DateTimeOffset? LockoutEndUtc { get; set; }
}
