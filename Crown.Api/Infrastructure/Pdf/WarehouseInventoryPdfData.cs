namespace Crown.Infrastructure.Pdf;

public sealed class WarehouseInventoryPdfData
{
    public OrganizationDocumentInfo Organization { get; init; } = new();

    public PdfDocumentLocale Locale { get; init; } = PdfDocumentLocale.Create(null, "RUB");

    public DateTime GeneratedAtLocal { get; init; }

    public string? FilterCaption { get; init; }

    public IReadOnlyList<WarehouseInventoryPdfRow> Rows { get; init; } = [];
}

public sealed class WarehouseInventoryPdfRow
{
    public string Name { get; init; } = string.Empty;

    public string? Code { get; init; }

    public string? Barcode { get; init; }

    public string? Category { get; init; }

    public string Unit { get; init; } = string.Empty;

    public decimal QuantityOnHand { get; init; }

    public decimal MinQuantityOnHand { get; init; }

    public decimal UnitPrice { get; init; }

    public bool IsArchived { get; init; }
}
