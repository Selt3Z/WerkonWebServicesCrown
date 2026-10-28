using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.Import;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Warehouse;

namespace Crown.Infrastructure.Import;

public sealed class WarehousePartImportService
{
    private const string RowErrorNameRequired = "name_required";
    private const string RowErrorCodeRequired = "code_required";
    private const string RowErrorUnitInvalid = "unit_invalid";
    private const string RowErrorPriceInvalid = "price_invalid";
    private const string RowErrorQtyInvalid = "qty_invalid";
    private const string RowErrorArchivedInvalid = "archived_invalid";

    private readonly AppDbContext _dbContext;
    private readonly WarehouseStockService _warehouseStockService;

    public WarehousePartImportService(AppDbContext dbContext, WarehouseStockService warehouseStockService)
    {
        _dbContext = dbContext;
        _warehouseStockService = warehouseStockService;
    }

    public SpreadsheetImportPreviewResponse Preview(byte[] data, string? fileName) =>
        SpreadsheetImportFileHelper.Preview(data, fileName);

    public async Task<SpreadsheetImportCommitResponse> CommitAsync(
        SpreadsheetImportCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        var data = SpreadsheetImportFileHelper.DecodeBase64(request.FileBase64);
        SpreadsheetImportFileHelper.ValidateFile(data, fileName: null);

        var response = new SpreadsheetImportCommitResponse();

        using var workbook = SpreadsheetImportFileHelper.OpenWorkbook(data);
        var sheet = workbook.Worksheets.First();
        var (lastRow, lastColumn) = SpreadsheetImportFileHelper.GetUsedBounds(sheet);

        // Маппинг с UI (индексы с 0) → столбцы Excel (с 1). Name+Code обязательны.
        var nameCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.NameColumn, lastColumn);
        var codeCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.CodeColumn, lastColumn);
        var barcodeCol = SpreadsheetImportFileHelper.ToExcelColumn(request.BarcodeColumn, lastColumn);
        var categoryCol = SpreadsheetImportFileHelper.ToExcelColumn(request.CategoryColumn, lastColumn);
        var priceCol = SpreadsheetImportFileHelper.ToExcelColumn(request.UnitPriceColumn, lastColumn);
        var unitCol = SpreadsheetImportFileHelper.ToExcelColumn(request.UnitColumn, lastColumn);
        var qtyCol = SpreadsheetImportFileHelper.ToExcelColumn(request.QuantityOnHandColumn, lastColumn);
        var minQtyCol = SpreadsheetImportFileHelper.ToExcelColumn(request.MinQuantityOnHandColumn, lastColumn);
        var descriptionCol = SpreadsheetImportFileHelper.ToExcelColumn(request.DescriptionColumn, lastColumn);
        var archivedCol = SpreadsheetImportFileHelper.ToExcelColumn(request.IsArchivedColumn, lastColumn);
        var activeCol = SpreadsheetImportFileHelper.ToExcelColumn(request.IsActiveColumn, lastColumn);

        EnsureDistinctRequired(request.NameColumn, request.CodeColumn);

        var existingCodes = await _dbContext.WarehouseParts
            .IgnoreQueryFilters()
            .Where(x => x.Code != null && x.Code != string.Empty)
            .Select(x => x.Code!)
            .ToListAsync(cancellationToken);

        var codeSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        for (var row = 2; row <= lastRow; row++)
        {
            var name = SpreadsheetImportFileHelper.ReadCell(sheet, row, nameCol);
            var code = SpreadsheetImportFileHelper.ReadCell(sheet, row, codeCol);
            var barcode = barcodeCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, barcodeCol.Value) : string.Empty;
            var category = categoryCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, categoryCol.Value) : string.Empty;
            var priceRaw = priceCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, priceCol.Value) : "0";
            var unitRaw = unitCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, unitCol.Value) : MeasurementUnits.Pieces;
            var qtyRaw = qtyCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, qtyCol.Value) : "0";
            var minQtyRaw = minQtyCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, minQtyCol.Value) : "1";
            var description = descriptionCol.HasValue
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, descriptionCol.Value)
                : string.Empty;
            var archivedRaw = archivedCol.HasValue
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, archivedCol.Value)
                : string.Empty;
            var activeRaw = activeCol.HasValue
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, activeCol.Value)
                : string.Empty;

            if (string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(code)
                && string.IsNullOrWhiteSpace(barcode)
                && string.IsNullOrWhiteSpace(category)
                && string.IsNullOrWhiteSpace(description))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                AddError(response, row, RowErrorNameRequired);
                continue;
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                AddError(response, row, RowErrorCodeRequired);
                continue;
            }

            var unit = string.IsNullOrWhiteSpace(unitRaw) ? MeasurementUnits.Pieces : unitRaw.Trim();
            if (!MeasurementUnits.IsAllowed(unit))
            {
                AddError(response, row, RowErrorUnitInvalid);
                continue;
            }

            if (!SpreadsheetImportFileHelper.TryParseDecimal(string.IsNullOrWhiteSpace(priceRaw) ? "0" : priceRaw, out var price)
                || price < 0)
            {
                AddError(response, row, RowErrorPriceInvalid);
                continue;
            }

            if (!SpreadsheetImportFileHelper.TryParseDecimal(string.IsNullOrWhiteSpace(qtyRaw) ? "0" : qtyRaw, out var qty)
                || qty < 0
                || !MeasurementUnits.IsValidQuantity(unit, qty))
            {
                AddError(response, row, RowErrorQtyInvalid);
                continue;
            }

            if (!SpreadsheetImportFileHelper.TryParseDecimal(string.IsNullOrWhiteSpace(minQtyRaw) ? "1" : minQtyRaw, out var minQty)
                || minQty < 0
                || !MeasurementUnits.IsValidQuantity(unit, minQty))
            {
                AddError(response, row, RowErrorQtyInvalid);
                continue;
            }

            var isArchived = false;
            if (!string.IsNullOrWhiteSpace(archivedRaw))
            {
                if (!SpreadsheetImportFileHelper.TryParseBool(archivedRaw, defaultValue: false, out isArchived))
                {
                    AddError(response, row, RowErrorArchivedInvalid);
                    continue;
                }
            }
            else if (!string.IsNullOrWhiteSpace(activeRaw))
            {
                if (!SpreadsheetImportFileHelper.TryParseBool(activeRaw, defaultValue: true, out var isActive))
                {
                    AddError(response, row, RowErrorArchivedInvalid);
                    continue;
                }

                isArchived = !isActive;
            }

            var normalizedCode = code.Trim();
            if (!codeSet.Add(normalizedCode))
            {
                response.Skipped++;
                continue;
            }

            qty = MeasurementUnits.NormalizeQuantity(unit, qty);
            minQty = MeasurementUnits.NormalizeQuantity(unit, minQty);

            var entity = new WarehousePart
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                Code = normalizedCode,
                Barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim(),
                Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                DefaultUnitPrice = price,
                DefaultUnit = unit,
                QuantityOnHand = qty,
                MinQuantityOnHand = minQty,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsArchived = isArchived,
                ArchivedAtUtc = isArchived ? now : null,
                CreatedAtUtc = now
            };

            _dbContext.WarehouseParts.Add(entity);

            if (qty > 0)
            {
                _warehouseStockService.RecordReceipt(entity, qty, userId: null, userDisplayName: "import");
            }

            response.Created++;
        }

        if (response.Created > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return response;
    }

    private static void EnsureDistinctRequired(int? nameColumn, int? codeColumn)
    {
        if (nameColumn is null || codeColumn is null || nameColumn == codeColumn)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }
    }

    private static void AddError(SpreadsheetImportCommitResponse response, int row, string code)
    {
        response.Errors++;
        if (response.ErrorRows.Count < SpreadsheetImportFileHelper.MaxErrorRowsReturned)
        {
            response.ErrorRows.Add(new SpreadsheetImportRowError { RowNumber = row, Code = code });
        }
    }
}
