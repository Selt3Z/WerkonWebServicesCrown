using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Export;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Export;

public sealed class DataExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly AppDbContext _dbContext;

    public DataExportService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<DataExportDocument> BuildDocumentAsync(CancellationToken cancellationToken = default)
    {
        var catalogParts = await _dbContext.CatalogParts
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new DataExportCatalogPartItem
            {
                Id = x.Id,
                OwnerUserId = x.OwnerUserId,
                Name = x.Name,
                Code = x.Code,
                Category = x.Category,
                DefaultUnitPrice = x.DefaultUnitPrice,
                DefaultUnit = x.DefaultUnit,
                Description = x.Description,
                SlaHours = x.SlaHours,
                IsActive = x.IsActive,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var warehouseParts = await _dbContext.WarehouseParts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Select(x => new DataExportWarehousePartItem
            {
                Id = x.Id,
                Name = x.Name,
                Code = x.Code,
                Barcode = x.Barcode,
                Category = x.Category,
                DefaultUnitPrice = x.DefaultUnitPrice,
                DefaultUnit = x.DefaultUnit,
                QuantityOnHand = x.QuantityOnHand,
                MinQuantityOnHand = x.MinQuantityOnHand,
                Description = x.Description,
                IsArchived = x.IsArchived,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new DataExportDocument
        {
            ExportVersion = DataExportDocument.CurrentVersion,
            ExportedAtUtc = DateTime.UtcNow,
            CatalogParts = catalogParts,
            WarehouseParts = warehouseParts
        };
    }

    public async Task<byte[]> BuildJsonAsync(CancellationToken cancellationToken = default)
    {
        var document = await BuildDocumentAsync(cancellationToken);
        return JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
    }

    public async Task<byte[]> BuildCsvZipAsync(CancellationToken cancellationToken = default)
    {
        var document = await BuildDocumentAsync(cancellationToken);

        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteCsvEntry(archive, "catalog_parts.csv", document.CatalogParts, MapCatalogPartRow, CatalogPartHeaders);
            WriteCsvEntry(archive, "warehouse_parts.csv", document.WarehouseParts, MapWarehousePartRow, WarehousePartHeaders);
            WriteManifestEntry(archive, document);
        }

        return memoryStream.ToArray();
    }

    private static void WriteManifestEntry(ZipArchive archive, DataExportDocument document)
    {
        var manifest = new
        {
            document.ExportVersion,
            document.ExportedAtUtc,
            document.AppName,
            files = new[]
            {
                "catalog_parts.csv",
                "warehouse_parts.csv"
            }
        };

        var entry = archive.CreateEntry("manifest.json");
        using var stream = entry.Open();
        stream.Write(JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions));
    }

    private static void WriteCsvEntry<T>(
        ZipArchive archive,
        string fileName,
        IReadOnlyList<T> rows,
        Func<T, string?[]> mapRow,
        string[] headers)
    {
        var entry = archive.CreateEntry(fileName);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        writer.WriteLine(string.Join(',', headers.Select(CsvEscape)));

        foreach (var row in rows)
        {
            writer.WriteLine(string.Join(',', mapRow(row).Select(CsvEscape)));
        }
    }

    private static string[] CatalogPartHeaders =>
    [
        "id", "owner_user_id", "name", "code", "category", "default_unit_price", "default_unit",
        "description", "sla_hours", "is_active", "created_at_utc"
    ];

    private static string[] WarehousePartHeaders =>
    [
        "id", "name", "code", "barcode", "category", "default_unit_price", "default_unit",
        "quantity_on_hand", "min_quantity_on_hand", "description", "is_archived", "created_at_utc"
    ];

    private static string?[] MapCatalogPartRow(DataExportCatalogPartItem x) =>
    [
        x.Id.ToString(), x.OwnerUserId.ToString(), x.Name, x.Code, x.Category,
        x.DefaultUnitPrice.ToString(CultureInfo.InvariantCulture), x.DefaultUnit,
        x.Description, x.SlaHours?.ToString(CultureInfo.InvariantCulture),
        x.IsActive.ToString(), FormatUtc(x.CreatedAtUtc)
    ];

    private static string?[] MapWarehousePartRow(DataExportWarehousePartItem x) =>
    [
        x.Id.ToString(), x.Name, x.Code, x.Barcode, x.Category,
        x.DefaultUnitPrice.ToString(CultureInfo.InvariantCulture), x.DefaultUnit,
        x.QuantityOnHand.ToString(CultureInfo.InvariantCulture),
        x.MinQuantityOnHand.ToString(CultureInfo.InvariantCulture),
        x.Description, x.IsArchived.ToString(), FormatUtc(x.CreatedAtUtc)
    ];

    private static string FormatUtc(DateTime value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static string CsvEscape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var needsQuotes = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return needsQuotes ? $"\"{escaped}\"" : escaped;
    }
}