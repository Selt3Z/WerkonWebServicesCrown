using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.Import;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Import;

/// <summary>Импорт справочника запчастей (CatalogParts) из .xlsx — как каталог услуг.</summary>
public sealed class CatalogPartImportService
{
    private const string RowErrorNameRequired = "name_required";
    private const string RowErrorUnitInvalid = "unit_invalid";
    private const string RowErrorPriceInvalid = "price_invalid";
    private const string RowErrorActiveInvalid = "active_invalid";

    private readonly AppDbContext _dbContext;

    public CatalogPartImportService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public SpreadsheetImportPreviewResponse Preview(byte[] data, string? fileName) =>
        SpreadsheetImportFileHelper.Preview(data, fileName);

    public async Task<SpreadsheetImportCommitResponse> CommitAsync(
        SpreadsheetImportCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.OwnerUserId is null || request.OwnerUserId == Guid.Empty)
        {
            throw new SpreadsheetImportException("owner_required");
        }

        var ownerUserId = request.OwnerUserId.Value;
        var data = SpreadsheetImportFileHelper.DecodeBase64(request.FileBase64);
        SpreadsheetImportFileHelper.ValidateFile(data, fileName: null);

        var response = new SpreadsheetImportCommitResponse();

        using var workbook = SpreadsheetImportFileHelper.OpenWorkbook(data);
        var sheet = workbook.Worksheets.First();
        var (lastRow, lastColumn) = SpreadsheetImportFileHelper.GetUsedBounds(sheet);

        var nameCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.NameColumn, lastColumn);
        var codeCol = SpreadsheetImportFileHelper.ToExcelColumn(request.CodeColumn, lastColumn);
        var categoryCol = SpreadsheetImportFileHelper.ToExcelColumn(request.CategoryColumn, lastColumn);
        var priceCol = SpreadsheetImportFileHelper.ToExcelColumn(request.UnitPriceColumn, lastColumn);
        var unitCol = SpreadsheetImportFileHelper.ToExcelColumn(request.UnitColumn, lastColumn);
        var descriptionCol = SpreadsheetImportFileHelper.ToExcelColumn(request.DescriptionColumn, lastColumn);
        var activeCol = SpreadsheetImportFileHelper.ToExcelColumn(request.IsActiveColumn, lastColumn);

        var existingCodes = await _dbContext.CatalogParts
            .Where(x => x.Code != null && x.Code != string.Empty)
            .Select(x => x.Code!)
            .ToListAsync(cancellationToken);

        var codeSet = new HashSet<string>(existingCodes, StringComparer.OrdinalIgnoreCase);
        var now = DateTime.UtcNow;

        for (var row = 2; row <= lastRow; row++)
        {
            var name = SpreadsheetImportFileHelper.ReadCell(sheet, row, nameCol);
            var code = codeCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, codeCol.Value) : string.Empty;
            var category = categoryCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, categoryCol.Value) : string.Empty;
            var priceRaw = priceCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, priceCol.Value) : "0";
            var unitRaw = unitCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, unitCol.Value) : MeasurementUnits.Pieces;
            var description = descriptionCol.HasValue
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, descriptionCol.Value)
                : string.Empty;
            var activeRaw = activeCol.HasValue ? SpreadsheetImportFileHelper.ReadCell(sheet, row, activeCol.Value) : string.Empty;

            if (string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(code)
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

            if (!SpreadsheetImportFileHelper.TryParseBool(activeRaw, defaultValue: true, out var isActive))
            {
                AddError(response, row, RowErrorActiveInvalid);
                continue;
            }

            var normalizedCode = string.IsNullOrWhiteSpace(code) ? null : code.Trim();
            if (normalizedCode is not null && !codeSet.Add(normalizedCode))
            {
                response.Skipped++;
                continue;
            }

            _dbContext.CatalogParts.Add(new CatalogPart
            {
                Id = Guid.NewGuid(),
                OwnerUserId = ownerUserId,
                Name = name.Trim(),
                Code = normalizedCode,
                Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                DefaultUnitPrice = price,
                DefaultUnit = unit,
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IsActive = isActive,
                CreatedAtUtc = now
            });

            response.Created++;
        }

        if (response.Created > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return response;
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
