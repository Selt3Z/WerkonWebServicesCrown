namespace Crown.Contracts.Export;

public sealed class DataExportDocument
{
    public const int CurrentVersion = 1;

    public int ExportVersion { get; set; } = CurrentVersion;

    public DateTime ExportedAtUtc { get; set; }

    public string AppName { get; set; } = "Crown";

    public List<DataExportCatalogPartItem> CatalogParts { get; set; } = [];

    public List<DataExportWarehousePartItem> WarehouseParts { get; set; } = [];
}

public sealed class DataExportCatalogPartItem
{
    public Guid Id { get; set; }

    public Guid OwnerUserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Category { get; set; }

    public decimal DefaultUnitPrice { get; set; }

    public string DefaultUnit { get; set; } = string.Empty;

    public string? Description { get; set; }

    public int? SlaHours { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class DataExportWarehousePartItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Code { get; set; }

    public string? Barcode { get; set; }

    public string? Category { get; set; }

    public decimal DefaultUnitPrice { get; set; }

    public string DefaultUnit { get; set; } = string.Empty;

    public decimal QuantityOnHand { get; set; }

    public decimal MinQuantityOnHand { get; set; }

    public string? Description { get; set; }

    public bool IsArchived { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}