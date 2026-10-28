namespace Crown.Contracts.Settings;

public sealed class UploadOrganizationLogoRequest
{
    public string ContentType { get; set; } = string.Empty;

    public string DataBase64 { get; set; } = string.Empty;
}
