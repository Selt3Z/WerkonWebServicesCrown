namespace Crown.Infrastructure.Pdf;

public sealed class OrganizationDocumentInfo
{
    public string OrganizationName { get; init; } = string.Empty;

    public string Phone { get; init; } = string.Empty;

    public string Email { get; init; } = string.Empty;

    public string Address { get; init; } = string.Empty;

    public string Inn { get; init; } = string.Empty;

    public string Kpp { get; init; } = string.Empty;

    public string Ogrn { get; init; } = string.Empty;

    public string BankAccount { get; init; } = string.Empty;

    public string Bik { get; init; } = string.Empty;

    public byte[]? LogoBytes { get; init; }
}
