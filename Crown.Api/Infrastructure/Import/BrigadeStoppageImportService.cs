using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Crown.Contracts.Common;
using Crown.Contracts.Import;
using Crown.Domain.Entities;
using Crown.Features.BrigadeRating;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Crown.Infrastructure.Import;

/// <summary>
/// Импорт остановок из xlsx с маппингом столбцов (как клиенты Ratchet / оборудование Crown).
/// Алгоритм как при ручном вводе/проверках: хеш-индексы справочников, find-or-create, batch SaveChanges.
/// </summary>
public sealed class BrigadeStoppageImportService
{
    private const string RowErrorDateRequired = "date_required";
    private const string RowErrorDateInvalid = "date_invalid";
    private const string RowErrorBrigadeRequired = "brigade_required";
    private const string RowErrorBrigadeUnknown = "brigade_unknown";
    private const string RowErrorStoppedAtRequired = "stopped_at_required";
    private const string RowErrorStoppedAtInvalid = "stopped_at_invalid";
    private const string RowErrorRestartedAtInvalid = "restarted_at_invalid";
    private const string RowErrorStoppedByRequired = "stopped_by_required";
    private const string RowErrorRemarksRequired = "remarks_required";
    private const string RowErrorWorkTooLong = "work_too_long";
    private const string RowErrorDuplicate = "duplicate";

    private const int CommitEvery = 40;
    private const int MaxWorkLength = 500;

    private static readonly Regex RemarkLinePrefix = new(
        @"^\s*\d+[.)]\s*",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Режет «1. … 2. … 3. …» даже в одной строке ячейки.</summary>
    private static readonly Regex RemarkNumberSplit = new(
        @"(?=\d+[.)]\s)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HasRemarkNumber = new(
        @"\d+[.)]\s",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public BrigadeStoppageImportService(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    public SpreadsheetImportPreviewResponse Preview(byte[] data, string? fileName) =>
        SpreadsheetImportFileHelper.Preview(data, fileName);

    public async Task<SpreadsheetImportCommitResponse> CommitAsync(
        SpreadsheetImportCommitRequest request,
        Guid createdByUserId,
        CancellationToken cancellationToken = default)
    {
        var data = SpreadsheetImportFileHelper.DecodeBase64(request.FileBase64);
        SpreadsheetImportFileHelper.ValidateFile(data, fileName: null);

        var response = new SpreadsheetImportCommitResponse();

        using var workbook = SpreadsheetImportFileHelper.OpenWorkbook(data);
        var sheet = workbook.Worksheets.First();
        var (lastRow, lastColumn) = SpreadsheetImportFileHelper.GetUsedBounds(sheet);

        var dateCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.ReportDateColumn, lastColumn);
        var brigadeCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.BrigadeColumn, lastColumn);
        var stoppedAtCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.StoppedAtColumn, lastColumn);
        var stoppedByCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.StoppedByColumn, lastColumn);
        var remarksCol = SpreadsheetImportFileHelper.RequireExcelColumn(request.RemarksColumn, lastColumn);
        var masterCol = SpreadsheetImportFileHelper.ToExcelColumn(request.MasterColumn, lastColumn);
        var restartedCol = SpreadsheetImportFileHelper.ToExcelColumn(request.RestartedAtColumn, lastColumn);
        var workCol = SpreadsheetImportFileHelper.ToExcelColumn(request.WorkColumn, lastColumn);

        // Обязательные столбцы должны быть разными (иначе маппинг бессмысленен).
        var required = new[] { dateCol, brigadeCol, stoppedAtCol, stoppedByCol, remarksCol };
        if (required.Distinct().Count() != required.Length)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }

        var brigadeIndex = await LoadBrigadeIndexAsync(cancellationToken);
        var actIndex = await CatalogTextLookup.LoadActIndexAsync(_db, cancellationToken);
        var workIndex = await CatalogTextLookup.LoadWorkIndexAsync(_db, cancellationToken);

        // Дубликаты внутри файла + уже существующие в БД за затронутый диапазон дат.
        var existingKeys = await LoadExistingKeysAsync(cancellationToken);
        var seenInFile = new HashSet<string>(StringComparer.Ordinal);

        var dirty = 0;
        var now = DateTimeOffset.UtcNow;

        for (var row = 2; row <= lastRow; row++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsRowBlank(sheet, row, dateCol, brigadeCol, stoppedAtCol, stoppedByCol, remarksCol, masterCol, restartedCol, workCol))
            {
                continue;
            }

            if (!SpreadsheetImportFileHelper.TryReadDateOnlyCell(sheet.Cell(row, dateCol), out var reportDate)
                || reportDate is null)
            {
                AddError(response, row, reportDate is null && sheet.Cell(row, dateCol).IsEmpty()
                    ? RowErrorDateRequired
                    : RowErrorDateInvalid);
                continue;
            }

            var brigadeRaw = SpreadsheetImportFileHelper.ReadCell(sheet, row, brigadeCol);
            if (string.IsNullOrWhiteSpace(brigadeRaw))
            {
                AddError(response, row, RowErrorBrigadeRequired);
                continue;
            }

            if (!TryResolveBrigade(brigadeIndex, brigadeRaw, out var brigade))
            {
                AddError(response, row, RowErrorBrigadeUnknown);
                continue;
            }

            if (!SpreadsheetImportFileHelper.TryReadTimeOnlyCell(sheet.Cell(row, stoppedAtCol), out var stoppedAt)
                || stoppedAt is null)
            {
                AddError(response, row, sheet.Cell(row, stoppedAtCol).IsEmpty()
                    ? RowErrorStoppedAtRequired
                    : RowErrorStoppedAtInvalid);
                continue;
            }

            TimeOnly? restartedAt = null;
            if (restartedCol is int rCol)
            {
                if (!SpreadsheetImportFileHelper.TryReadTimeOnlyCell(sheet.Cell(row, rCol), out restartedAt))
                {
                    AddError(response, row, RowErrorRestartedAtInvalid);
                    continue;
                }
            }

            var stoppedBy = SpreadsheetImportFileHelper.ReadCell(sheet, row, stoppedByCol).Trim();
            if (string.IsNullOrWhiteSpace(stoppedBy))
            {
                AddError(response, row, RowErrorStoppedByRequired);
                continue;
            }

            var masterRaw = masterCol is int mCol
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, mCol).Trim()
                : string.Empty;
            var masterName = string.IsNullOrWhiteSpace(masterRaw) ? brigade.DisplayName : masterRaw;

            var workRaw = workCol is int wCol
                ? SpreadsheetImportFileHelper.ReadCell(sheet, row, wCol).Trim()
                : string.Empty;
            if (workRaw.Length > MaxWorkLength)
            {
                AddError(response, row, RowErrorWorkTooLong);
                continue;
            }

            // GetString — чтобы не схлопнуть «1.\n2.\n3.» в одну строку.
            var remarksRaw = ReadRemarkCell(sheet, row, remarksCol);
            var remarkTexts = ParseRemarkLines(remarksRaw);
            if (remarkTexts.Count == 0)
            {
                AddError(response, row, RowErrorRemarksRequired);
                continue;
            }

            var dedupeKey = BuildDedupeKey(brigade.Id, reportDate.Value, stoppedAt.Value, masterName, stoppedBy);
            if (!seenInFile.Add(dedupeKey) || existingKeys.Contains(dedupeKey))
            {
                AddError(response, row, RowErrorDuplicate);
                continue;
            }

            Guid? workItemId = null;
            var workDescription = workRaw;
            if (workDescription.Length > 0)
            {
                workItemId = await ResolveOrCreateWorkAsync(workIndex, workDescription, cancellationToken);
                workDescription = workIndex.ById[workItemId.Value].Text.Trim();
                if (workDescription.Length > MaxWorkLength)
                {
                    AddError(response, row, RowErrorWorkTooLong);
                    continue;
                }
            }

            var entity = new BrigadeStoppage
            {
                Id = Guid.NewGuid(),
                BrigadeUserId = brigade.Id,
                BrigadeName = brigade.DisplayName,
                MasterName = masterName,
                MasterPersonId = null,
                ReportDate = reportDate.Value,
                StoppedByUserId = null,
                StoppedByPersonId = null,
                StoppedByName = stoppedBy,
                StoppedAt = stoppedAt.Value,
                RestartedAt = restartedAt,
                Hours = BrigadeStoppageHours.Compute(stoppedAt.Value, restartedAt),
                WorkDescription = workDescription,
                WorkItemId = workItemId,
                IsWorkManual = false,
                CreatedAt = now,
                CreatedByUserId = createdByUserId,
                LastModifiedAt = now,
                LastModifiedByUserId = createdByUserId
            };

            var sort = 0;
            foreach (var (text, comment) in remarkTexts)
            {
                // Только привязка к уже существующему пункту — в справочник ничего не пишем.
                Guid? actId = null;
                var snapshot = text.Trim();
                var isManual = true;
                if (actIndex.ByKey.TryGetValue(CatalogTextLookup.NormKey(text), out var actLite))
                {
                    actId = actLite.Id;
                    snapshot = actLite.Text.Trim();
                    isManual = false;
                }

                sort++;
                entity.Remarks.Add(new StoppageRemark
                {
                    Id = Guid.NewGuid(),
                    ActItemId = actId,
                    Text = snapshot,
                    Comment = comment,
                    IsManual = isManual,
                    IsResolved = false,
                    SortOrder = sort
                });
            }

            _db.BrigadeStoppages.Add(entity);
            existingKeys.Add(dedupeKey);
            response.Created++;
            dirty++;

            if (dirty >= CommitEvery)
            {
                await _db.SaveChangesAsync(cancellationToken);
                dirty = 0;
            }
        }

        if (dirty > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return response;
    }

    private async Task<Dictionary<string, BrigadeLite>> LoadBrigadeIndexAsync(CancellationToken cancellationToken)
    {
        var roleUsers = await _users.GetUsersInRoleAsync(AppRoles.Brigade);
        var index = new Dictionary<string, BrigadeLite>(StringComparer.Ordinal);

        void AddKey(string? raw, BrigadeLite lite)
        {
            var key = CatalogTextLookup.NormKey(raw);
            if (string.IsNullOrEmpty(key) || index.ContainsKey(key))
            {
                return;
            }

            index[key] = lite;
        }

        foreach (var u in roleUsers.Where(x => x.IsActive))
        {
            var display = UserDisplay.FormatBrigadeDisplay(u);
            var lite = new BrigadeLite(u.Id, display);
            AddKey(display, lite);
            AddKey(u.DisplayName, lite);
            AddKey(u.UserName, lite);
            AddKey(u.BrigadeNumber, lite);
            if (!string.IsNullOrWhiteSpace(u.BrigadeNumber))
            {
                var num = u.BrigadeNumber.Trim();
                AddKey($"№{num}", lite);
                AddKey($"#{num}", lite);

                // Excel часто «КРС-219» / «ПРС-7», а в Users — «Бригада КРС» + BrigadeNumber=219.
                foreach (var type in BrigadeTypeTokens(u.DisplayName, u.BrigadeWorkshop))
                {
                    AddKey($"{type}-{num}", lite);
                    AddKey($"{type}{num}", lite);
                    AddKey($"{type} {num}", lite);
                    AddKey($"{type}№{num}", lite);
                }
            }
        }

        return index;
    }

    /// <summary>Токены типа бригады из «Бригада КРС» / цеха «ПРС» → КРС, ПРС.</summary>
    private static IEnumerable<string> BrigadeTypeTokens(string? displayName, string? workshop)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();

        foreach (var raw in new[] { displayName, workshop })
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            // Последнее слово: «Бригада КРС» → КРС; цех «ПРС» → ПРС.
            var parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var candidate = parts.Length == 0 ? raw.Trim() : parts[^1];
            if (candidate.Length is < 2 or > 12 || !candidate.All(char.IsLetter))
            {
                continue;
            }

            if (string.Equals(candidate, "Бригада", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(candidate))
            {
                list.Add(candidate);
            }
        }

        return list;
    }

    private async Task<HashSet<string>> LoadExistingKeysAsync(CancellationToken cancellationToken)
    {
        // Ключ без Remarks — достаточно для «та же остановка уже есть».
        var rows = await _db.BrigadeStoppages.AsNoTracking()
            .Select(x => new
            {
                x.BrigadeUserId,
                x.ReportDate,
                x.StoppedAt,
                x.MasterName,
                x.StoppedByName
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(x => BuildDedupeKey(x.BrigadeUserId, x.ReportDate, x.StoppedAt, x.MasterName, x.StoppedByName))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static readonly Regex BrigadeCodeNumber = new(
        @"^\s*(?<type>[A-Za-zА-Яа-яЁё]{2,12})\s*[-–—№#.\s]+\s*(?<num>\d{1,6})\s*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static bool TryResolveBrigade(
        IReadOnlyDictionary<string, BrigadeLite> index,
        string raw,
        out BrigadeLite brigade)
    {
        var key = CatalogTextLookup.NormKey(raw);
        if (index.TryGetValue(key, out brigade!))
        {
            return true;
        }

        // «Имя №12» / хвост с номером — пробуем номер отдельно.
        var trimmed = raw.Trim();
        var noIdx = trimmed.LastIndexOf('№');
        if (noIdx >= 0 && noIdx < trimmed.Length - 1)
        {
            var numKey = CatalogTextLookup.NormKey(trimmed[(noIdx + 1)..]);
            if (index.TryGetValue(numKey, out brigade!))
            {
                return true;
            }
        }

        // «КРС-219» / «ПРС 7» → ключи type-num и чистый номер.
        var m = BrigadeCodeNumber.Match(trimmed);
        if (m.Success)
        {
            var type = m.Groups["type"].Value;
            var num = m.Groups["num"].Value;
            foreach (var candidate in new[]
                     {
                         $"{type}-{num}",
                         $"{type}{num}",
                         $"{type} {num}",
                         num
                     })
            {
                var ck = CatalogTextLookup.NormKey(candidate);
                if (index.TryGetValue(ck, out brigade!))
                {
                    return true;
                }
            }
        }

        brigade = null!;
        return false;
    }

    private async Task<Guid> ResolveOrCreateWorkAsync(
        CatalogTextLookup.WorkCatalogIndex index,
        string text,
        CancellationToken cancellationToken)
    {
        var key = CatalogTextLookup.NormKey(text);
        if (index.ByKey.TryGetValue(key, out var lite))
        {
            if (!lite.IsActive)
            {
                await CatalogTextLookup.EnsureWorkActiveAsync(_db, lite, cancellationToken);
                lite.IsActive = true;
            }

            return lite.Id;
        }

        index.MaxSortOrder++;
        var entity = new StoppageWorkItem
        {
            Id = Guid.NewGuid(),
            Text = text.Trim(),
            IsActive = true,
            SortOrder = index.MaxSortOrder,
            CreatedAt = DateTimeOffset.UtcNow
        };
        _db.StoppageWorkItems.Add(entity);
        lite = new CatalogTextLookup.WorkCatalogLite
        {
            Id = entity.Id,
            Text = entity.Text,
            IsActive = true,
            SortOrder = entity.SortOrder
        };
        index.ById[entity.Id] = lite;
        index.ByKey[key] = lite;
        return entity.Id;
    }

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
            // fallback ниже
        }

        return SpreadsheetImportFileHelper.ReadCell(sheet, row, column);
    }

    /// <summary>
    /// Разбор пунктов: переносы строк и/или маркеры «1.» «2)» в одной ячейке.
    /// Не создаёт записей справочника — только режет текст.
    /// </summary>
    internal static List<(string Text, string Comment)> ParseRemarkLines(string? raw)
    {
        var result = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return result;
        }

        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

        IEnumerable<string> chunks;
        if (HasRemarkNumber.IsMatch(text))
        {
            // «1. a 2. b» / «1. a\n2. b» → отдельные куски по lookahead номера.
            chunks = RemarkNumberSplit.Split(text)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0);
        }
        else
        {
            chunks = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        foreach (var chunk in chunks)
        {
            var body = RemarkLinePrefix.Replace(chunk, string.Empty).Trim();
            if (body.Length == 0)
            {
                continue;
            }

            var comment = string.Empty;
            if (body.EndsWith(')') && body.Contains('(', StringComparison.Ordinal))
            {
                var open = body.LastIndexOf('(');
                if (open > 0)
                {
                    comment = body[(open + 1)..^1].Trim();
                    body = body[..open].Trim();
                }
            }

            if (body.Length == 0)
            {
                continue;
            }

            result.Add((body, comment));
        }

        return result;
    }

    private static string BuildDedupeKey(
        Guid brigadeUserId,
        DateOnly reportDate,
        TimeOnly stoppedAt,
        string masterName,
        string stoppedBy) =>
        string.Join(
            '|',
            brigadeUserId.ToString("N"),
            reportDate.ToString("yyyy-MM-dd"),
            stoppedAt.ToString("HH:mm"),
            CatalogTextLookup.NormKey(masterName),
            CatalogTextLookup.NormKey(stoppedBy));

    private static bool IsRowBlank(
        IXLWorksheet sheet,
        int row,
        int dateCol,
        int brigadeCol,
        int stoppedAtCol,
        int stoppedByCol,
        int remarksCol,
        int? masterCol,
        int? restartedCol,
        int? workCol)
    {
        static bool Empty(IXLWorksheet s, int r, int c) =>
            string.IsNullOrWhiteSpace(SpreadsheetImportFileHelper.ReadCell(s, r, c)) && s.Cell(r, c).IsEmpty();

        if (!Empty(sheet, row, dateCol)
            || !Empty(sheet, row, brigadeCol)
            || !Empty(sheet, row, stoppedAtCol)
            || !Empty(sheet, row, stoppedByCol)
            || !Empty(sheet, row, remarksCol))
        {
            return false;
        }

        if (masterCol is int m && !Empty(sheet, row, m))
        {
            return false;
        }

        if (restartedCol is int r && !Empty(sheet, row, r))
        {
            return false;
        }

        if (workCol is int w && !Empty(sheet, row, w))
        {
            return false;
        }

        return true;
    }

    private static void AddError(SpreadsheetImportCommitResponse response, int row, string code)
    {
        response.Errors++;
        response.Skipped++;
        if (response.ErrorRows.Count < SpreadsheetImportFileHelper.MaxErrorRowsReturned)
        {
            response.ErrorRows.Add(new SpreadsheetImportRowError { RowNumber = row, Code = code });
        }
    }

    private sealed record BrigadeLite(Guid Id, string DisplayName);
}
