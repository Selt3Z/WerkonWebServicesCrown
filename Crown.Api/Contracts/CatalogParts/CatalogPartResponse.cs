namespace Crown.Contracts.CatalogParts;

public sealed class CatalogPartResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Category { get; set; }

    public decimal DefaultUnitPrice { get; set; }

    public string DefaultUnit { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
