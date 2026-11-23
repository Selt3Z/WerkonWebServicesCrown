using ClosedXML.Excel;
using Crown.Contracts.Common;
using Crown.Contracts.Import;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crown.Infrastructure.Import;

/// <summary>
/// Импорт проверок/ППК из xlsx (колонки как у экспорта Супервайзинга).
/// Матчинг бригад — тот же, что у остановок; пункты акта не создаём в справочнике.
/// </summary>
public sealed class BrigadeInspectionImportService(
    AppDbContext db,
    BrigadeStoppageImportService stoppageImport)
{
    private const string RowErrorDateRequired = "date_required";
    private const string RowErrorDateInvalid = "date_invalid";
    private const string RowErrorBrigadeRequired = "brigade_required";
    private const string RowErrorBrigadeUnknown = "brigade_unknown";
    private const string RowErrorCheckerRequired = "checker_required";
    private const string RowErrorOutcomeRequired = "outcome_required";
    private const string RowErrorOutcomeInvalid = "outcome_invalid";
    private const string RowErrorRemarksRequired = "remarks_required";
    private const string RowErrorDuplicate = "duplicate";
    private const int CommitEvery = 40;

    public SpreadsheetImportPreviewResponse Preview(byte[] data, string? fileName) =>
        SpreadsheetImportFileHelper.Preview(data, fileName);

    public async Task<SpreadsheetImportCommitResponse> CommitAsync(
        SpreadsheetImportCommitRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseKind(request.InspectionKind, out var kind))
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }

        // v1 UI — ППК; API принимает Kind на будущее.
        if (kind is not (BrigadeInspectionKind.Ppk or BrigadeInspectionKind.Supervising))
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }

        var data = SpreadsheetImportFileHelper.DecodeBase64(request.FileBase64);
        SpreadsheetImportFileHelper.ValidateFile(data, fileName: null);

        var response = new SpreadsheetImportCommitResponse();
        using var workbook = SpreadsheetImportFileHelper.OpenWorkbook(data);
        var sheet = workbook.Worksheets.First();
        var (lastRow, lastColumn) = SpreadsheetImportFileHelper.GetUsedBounds(sheet);

        var dateCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.ReportDateColumn, lastColumn);
        var brigadeCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.BrigadeColumn, lastColumn);
        var masterCol = SpreadsheetImportFileHelper.ToExcelColumn(request.MasterColumn, lastColumn);
        var outcomeCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.OutcomeColumn, lastColumn);
        var remarksCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.RemarksColumn, lastColumn);
        var checkerCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.CheckerColumn, lastColumn);
        var wellCol = SpreadsheetImportFileHelper.ToExcelColumn(request.WellColumn, lastColumn);
        var checkerPosCol = SpreadsheetImportFileHelper.ToExcelColumn(request.CheckerPositionColumn, lastColumn);
        var stopPointCol = SpreadsheetImportFileHelper.ToExcelColumn(request.StopPointColumn, lastColumn);
        var hasExplCol = SpreadsheetImportFileHelper.ToExcelColumn(request.HasExplanationColumn, lastColumn);
        var fixDateCol = SpreadsheetImportFileHelper.ToExcelColumn(request.FixDateColumn, lastColumn);

        var required = new[] { dateCol, brigadeCol, outcomeCol, remarksCol, checkerCol };
        if (required.Distinct().Count() != required.Length)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }

        // Опциональный мастер не должен совпадать с обязательными столбцами.
        if (masterCol is int mc && required.Contains(mc))
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }

        var brigadeIndex = await stoppageImport.LoadBrigadeIndexAsync(cancellationToken);
        var actIndex = await CatalogTextLookup.LoadActIndexAsync(db, cancellationToken);
        var existingKeys = await LoadExistingKeysAsync(kind, cancellationToken);
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);

        var nextSeq = await db.BrigadeInspections.AsNoTracking()
            .Where(x => x.Kind == kind)
            .MaxAsync(x => (int?)x.SequenceNo, cancellationToken) ?? 0;

        var dirty = 0;
        var now = DateTimeOffset.UtcNow;

        for (var row = 2; row <= lastRow; row++)
        {
            if (IsRowBlank(sheet, row, dateCol, brigadeCol, outcomeCol, remarksCol, checkerCol))
            {
                continue;
            }

            DateOnly? reportDate = null;
            if (!SpreadsheetImportFileHelper.TryReadDateOnlyCell(sheet.Cell(row, dateCol), out reportDate)
                || reportDate is null)
            {
                var dateRaw = SpreadsheetImportFileHelper.ReadCell(sheet, row, dateCol);
                if (string.IsNullOrWhiteSpace(dateRaw))
                {
                    AddError(response, row, RowErrorDateRequired);
                    continue;
                }

                if (!SpreadsheetImportFileHelper.TryParseDateOnly(dateRaw, out reportDate) || reportDate is null)
                {
                    AddError(response, row, RowErrorDateInvalid);
                    continue;
                }
            }

            var brigadeRaw = SpreadsheetImportFileHelper.ReadCell(sheet, row, brigadeCol);
            if (string.IsNullOrWhiteSpace(brigadeRaw))
            {
                AddError(response, row, RowErrorBrigadeRequired);
                continue;
            }

            if (!BrigadeStoppageImportService.TryResolveBrigade(brigadeIndex, brigadeRaw, out var brigade))
            {
                AddError(response, row, RowErrorBrigadeUnknown);
                continue;
            }

            var masterName = masterCol is int mCol
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, mCol).Trim()
                : string.Empty;

            var checkerName = SpreadsheetImportFileHelper.ReadCell(sheet, row, checkerCol).Trim();
            if (checkerName.Length == 0)
            {
                AddError(response, row, RowErrorCheckerRequired);
                continue;
            }

            var outcomeRaw = SpreadsheetImportFileHelper.ReadCell(sheet, row, outcomeCol);
            if (string.IsNullOrWhiteSpace(outcomeRaw))
            {
                AddError(response, row, RowErrorOutcomeRequired);
                continue;
            }

            if (!TryParseOutcome(outcomeRaw, out var outcome))
            {
                AddError(response, row, RowErrorOutcomeInvalid);
                continue;
            }

            var remarkRaw = ReadRemarkCell(sheet, row, remarksCol);
            var remarkLines = BrigadeStoppageImportService.ParseRemarkLines(remarkRaw);
            if (remarkLines.Count == 0)
            {
                AddError(response, row, RowErrorRemarksRequired);
                continue;
            }

            var dedupeKey = BuildDedupeKey(
                kind,
                brigade.Id,
                reportDate.Value,
                masterName,
                checkerName,
                outcome);
            if (!seenInFile.Add(dedupeKey) || existingKeys.Contains(dedupeKey))
            {
                AddError(response, row, RowErrorDuplicate);
                response.Skipped++;
                continue;
            }

            existingKeys.Add(dedupeKey);
            nextSeq++;

            var well = wellCol is int wc
                ? NullIfEmpty(SpreadsheetImportFileHelper.ReadCell(sheet, row, wc))
                : null;
            var checkerPos = checkerPosCol is int cpc
                ? NullIfEmpty(SpreadsheetImportFileHelper.ReadCell(sheet, row, cpc))
                : null;
            var stopPoint = stopPointCol is int spc && ParseYesNo(SpreadsheetImportFileHelper.ReadCell(sheet, row, spc));
            var hasExplanation = hasExplCol is int hec && ParseYesNo(SpreadsheetImportFileHelper.ReadCell(sheet, row, hec));

            DateOnly? fixDate = null;
            string? fixDateManual = null;
            var fixDateIsManual = false;
            if (fixDateCol is int fdc)
            {
                if (SpreadsheetImportFileHelper.TryReadDateOnlyCell(sheet.Cell(row, fdc), out var fd) && fd is not null)
                {
                    fixDate = fd;
                }
                else
                {
                    var fixRaw = SpreadsheetImportFileHelper.ReadCell(sheet, row, fdc).Trim();
                    if (fixRaw.Length > 0)
                    {
                        if (SpreadsheetImportFileHelper.TryParseDateOnly(fixRaw, out var parsed) && parsed is not null)
                        {
                            fixDate = parsed;
                        }
                        else
                        {
                            fixDateIsManual = true;
                            fixDateManual = fixRaw;
                        }
                    }
                }
            }

            var entity = new BrigadeInspection
            {
                Id = Guid.NewGuid(),
                Kind = kind,
                SequenceNo = nextSeq,
                ReportDate = reportDate.Value,
                Workshop = (brigade.Workshop ?? string.Empty).Trim(),
                BrigadeUserId = brigade.Id,
                BrigadeNumberSnapshot = (brigade.BrigadeNumber ?? string.Empty).Trim(),
                MasterName = masterName,
                Well = well,
                CheckerName = checkerName,
                CheckerPosition = checkerPos,
                Outcome = outcome,
                StopPoint = stopPoint,
                HasExplanation = hasExplanation,
                FixDate = fixDate,
                FixDateIsManual = fixDateIsManual,
                FixDateManualText = fixDateManual,
                CreatedAt = now
            };

            var sort = 0;
            foreach (var (text, comment) in remarkLines)
            {
                Guid? actId = null;
                var isManual = true;
                var snapshot = text.Trim();
                if (actIndex.ByKey.TryGetValue(CatalogTextLookup.NormKey(text), out var actLite))
                {
                    actId = actLite.Id;
                    snapshot = actLite.Text.Trim();
                    isManual = false;
                }

                entity.Remarks.Add(new BrigadeInspectionRemark
                {
                    Id = Guid.NewGuid(),
                    InspectionId = entity.Id,
                    ActItemId = actId,
                    Text = snapshot,
                    Comment = NullIfEmpty(comment),
                    IsManual = isManual,
                    SortOrder = sort++
                });
            }

            db.BrigadeInspections.Add(entity);
            response.Created++;
            dirty++;

            if (dirty >= CommitEvery)
            {
                await db.SaveChangesAsync(cancellationToken);
                dirty = 0;
            }
        }

        if (dirty > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return response;
    }

    private async Task<HashSet<string>> LoadExistingKeysAsync(
        BrigadeInspectionKind kind,
        CancellationToken cancellationToken)
    {
        var rows = await db.BrigadeInspections.AsNoTracking()
            .Where(x => x.Kind == kind)
            .Select(x => new
            {
                x.BrigadeUserId,
                x.ReportDate,
                x.MasterName,
                x.CheckerName,
                x.Outcome
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => BuildDedupeKey(kind, x.BrigadeUserId, x.ReportDate, x.MasterName, x.CheckerName, x.Outcome))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string BuildDedupeKey(
        BrigadeInspectionKind kind,
        Guid brigadeUserId,
        DateOnly reportDate,
        string masterName,
        string checkerName,
        BrigadeInspectionOutcome? outcome) =>
        string.Join('|',
            ((int)kind).ToString(),
            brigadeUserId.ToString("N"),
            reportDate.ToString("yyyy-MM-dd"),
            CatalogTextLookup.NormKey(masterName),
            CatalogTextLookup.NormKey(checkerName),
            outcome is null ? "" : ((int)outcome.Value).ToString());

    private static bool TryParseKind(string? raw, out BrigadeInspectionKind kind)
    {
        if (Enum.TryParse(raw?.Trim(), ignoreCase: true, out kind) && Enum.IsDefined(kind))
        {
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TryParseOutcome(string raw, out BrigadeInspectionOutcome outcome)
    {
        var key = CatalogTextLookup.NormKey(raw);
        if (key is "предписание" or "п" or "prescription")
        {
            outcome = BrigadeInspectionOutcome.Prescription;
            return true;
        }

        if (key is "замечание" or "з" or "remark")
        {
            outcome = BrigadeInspectionOutcome.Remark;
            return true;
        }

        if (key is "остановка" or "о" or "stoppage")
        {
            outcome = BrigadeInspectionOutcome.Stoppage;
            return true;
        }

        // «П/З/О» и комбинированные ярлыки из экспорта.
        if (key.Contains("предписан", StringComparison.Ordinal))
        {
            outcome = BrigadeInspectionOutcome.Prescription;
            return true;
        }

        if (key.Contains("замечан", StringComparison.Ordinal))
        {
            outcome = BrigadeInspectionOutcome.Remark;
            return true;
        }

        if (key.Contains("останов", StringComparison.Ordinal))
        {
            outcome = BrigadeInspectionOutcome.Stoppage;
            return true;
        }

        outcome = default;
        return false;
    }

    private static bool ParseYesNo(string? raw)
    {
        var key = CatalogTextLookup.NormKey(raw);
        return key is "да" or "yes" or "y" or "1" or "true" or "+";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string ReadRemarkCell(IXLWorksheet sheet, int row, int column)
    {
        var cell = sheet.Cell(row, column);
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        try
        {
            var s = cell.GetString();
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s.Trim();
            }
        }
        catch
        {
            // fallback
        }

        return SpreadsheetImportFileHelper.ReadCell(sheet, row, column);
    }

    private static bool IsRowBlank(IXLWorksheet sheet, int row, params int[] cols) =>
        cols.All(c => string.IsNullOrWhiteSpace(SpreadsheetImportFileHelper.ReadCell(sheet, row, c)));

    private static void AddError(SpreadsheetImportCommitResponse response, int row, string code)
    {
        response.Errors++;
        if (response.ErrorRows.Count < SpreadsheetImportFileHelper.MaxErrorRowsReturned)
        {
            response.ErrorRows.Add(new SpreadsheetImportRowError { RowNumber = row, Code = code });
        }
    }
}
