using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.Import;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Import;

public sealed class EquipmentAssetImportService
{
    private const string RowErrorNameRequired = "name_required";
    private const string RowErrorInvRequired = "inventory_required";
    private const string RowErrorInvDuplicate = "inventory_duplicate";
    private const string RowErrorLocationRequired = "location_required";
    private const string RowErrorLocationUnknown = "location_unknown";

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public EquipmentAssetImportService(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
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

        var nameCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.NameColumn, lastColumn);
        var invCol = SpreadsheetImportFileHelper.RequireExcelColumn(
            request.InventoryNumberColumn ?? request.CodeColumn,
            lastColumn);
        var categoryCol = SpreadsheetImportFileHelper.ToExcelColumn(request.CategoryColumn, lastColumn);
        var descriptionCol = SpreadsheetImportFileHelper.ToExcelColumn(request.DescriptionColumn, lastColumn);
        var locationCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.LocationColumn, lastColumn);

        if (request.NameColumn == (request.InventoryNumberColumn ?? request.CodeColumn))
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }

        var deptUsers = (await _users.GetUsersInRoleAsync(AppRoles.Department))
            .Where(x => x.IsActive)
            .ToList();

        var existingInv = await _db.EquipmentAssets
            .IgnoreQueryFilters()
            .Select(x => x.InventoryNumber)
            .ToListAsync(cancellationToken);
        var invSet = new HashSet<string>(existingInv, StringComparer.OrdinalIgnoreCase);

        var now = DateTime.UtcNow;
        for (var row = 2; row <= lastRow; row++)
        {
            var name = SpreadsheetImportFileHelper.ReadCell(sheet, row, nameCol);
            var inv = SpreadsheetImportFileHelper.ReadCell(sheet, row, invCol);
            var category = categoryCol.HasValue
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, categoryCol.Value)
                : string.Empty;
            var description = descriptionCol.HasValue
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, descriptionCol.Value)
                : string.Empty;
            var location = SpreadsheetImportFileHelper.ReadCell(sheet, row, locationCol);

            if (string.IsNullOrWhiteSpace(name)
                && string.IsNullOrWhiteSpace(inv)
                && string.IsNullOrWhiteSpace(location)
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

            if (string.IsNullOrWhiteSpace(inv))
            {
                AddError(response, row, RowErrorInvRequired);
                continue;
            }

            inv = inv.Trim();
            if (!invSet.Add(inv))
            {
                AddError(response, row, RowErrorInvDuplicate);
                continue;
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                AddError(response, row, RowErrorLocationRequired);
                continue;
            }

            var locKey = location.Trim();
            var dept = deptUsers.FirstOrDefault(u =>
                string.Equals((u.DisplayName ?? string.Empty).Trim(), locKey, StringComparison.OrdinalIgnoreCase)
                || string.Equals((u.UserName ?? string.Empty).Trim(), locKey, StringComparison.OrdinalIgnoreCase));
            if (dept is null)
            {
                AddError(response, row, RowErrorLocationUnknown);
                continue;
            }

            var label = string.IsNullOrWhiteSpace(dept.DisplayName)
                ? (dept.UserName ?? locKey)
                : dept.DisplayName.Trim();

            _db.EquipmentAssets.Add(new EquipmentAsset
            {
                Id = Guid.NewGuid(),
                Name = name.Trim(),
                InventoryNumber = inv,
                Category = string.IsNullOrWhiteSpace(category) ? null : category.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                CurrentLocationUserId = dept.Id,
                CurrentLocationLabel = label,
                OwnerUserId = dept.Id,
                OwnerLabel = label,
                Availability = EquipmentAvailability.Free,
                CreatedAtUtc = now
            });
            response.Created++;
        }

        if (response.Created > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return response;
    }

    private static void AddError(SpreadsheetImportCommitResponse response, int row, string code)
    {
        response.ErrorRows.Add(new SpreadsheetImportRowError { RowNumber = row, Code = code });
        response.Errors++;
        response.Skipped++;
    }
}
