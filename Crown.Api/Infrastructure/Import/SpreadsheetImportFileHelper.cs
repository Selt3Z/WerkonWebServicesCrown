using System.Globalization;
using System.IO.Compression;
using ClosedXML.Excel;
using Crown.Contracts.Common;
using Crown.Contracts.Import;

namespace Crown.Infrastructure.Import;

/// <summary>Общая валидация/чтение xlsx для импортов (каталог / склад).</summary>
public static class SpreadsheetImportFileHelper
{
    public const int MaxFileBytes = 5 * 1024 * 1024;
    public const int MaxSampleRows = 20;
    public const int MaxDataRows = 10_000;
    public const int MaxColumns = 50;
    public const int MaxErrorRowsReturned = 100;
    public const int MaxCellPreviewLength = 300;

    private const long MaxUncompressedBytes = 100L * 1024 * 1024;
    private const int MaxZipEntries = 2000;

    /// <summary>Декодирует base64 с проверкой размера ещё до выделения памяти под результат.</summary>
    public static byte[] DecodeBase64(string? dataBase64)
    {
        if (string.IsNullOrWhiteSpace(dataBase64))
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
        }

        var trimmed = dataBase64.Trim();

        // base64 раздувает данные в 4/3 раза; лишнее отсекаем до декодирования.
        if (trimmed.Length > (MaxFileBytes / 3 + 1) * 4)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportFileTooLarge);
        }

        try
        {
            return Convert.FromBase64String(trimmed);
        }
        catch (FormatException)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
        }
    }

    public static SpreadsheetImportPreviewResponse Preview(byte[] data, string? fileName)
    {
        ValidateFile(data, fileName);

        using var workbook = OpenWorkbook(data);
        var sheet = workbook.Worksheets.First();
        var (lastRow, lastColumn) = GetUsedBounds(sheet);

        var response = new SpreadsheetImportPreviewResponse();

        for (var column = 1; column <= lastColumn; column++)
        {
            response.Headers.Add(ReadCell(sheet, 1, column, MaxCellPreviewLength));
        }

        for (var row = 2; row <= lastRow; row++)
        {
            var values = new List<string>(lastColumn);

            for (var column = 1; column <= lastColumn; column++)
            {
                values.Add(ReadCell(sheet, row, column, MaxCellPreviewLength));
            }

            if (values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            response.TotalRows++;

            if (response.SampleRows.Count < MaxSampleRows)
            {
                response.SampleRows.Add(values);
            }
        }

        return response;
    }

    public static void ValidateFile(byte[] data, string? fileName)
    {
        if (data.Length == 0)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
        }

        if (data.Length > MaxFileBytes)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportFileTooLarge);
        }

        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName.Trim());

            if (string.Equals(extension, ".xls", StringComparison.OrdinalIgnoreCase))
            {
                throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportXlsNotSupported);
            }

            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
            }
        }

        if (IsLegacyXls(data))
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportXlsNotSupported);
        }

        if (!HasZipSignature(data))
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
        }

        ValidateZipStructure(data);
    }

    public static XLWorkbook OpenWorkbook(byte[] data)
    {
        try
        {
            return new XLWorkbook(new MemoryStream(data, writable: false));
        }
        catch (SpreadsheetImportException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
        }
    }

    public static (int LastRow, int LastColumn) GetUsedBounds(IXLWorksheet sheet)
    {
        var lastCell = sheet.LastCellUsed();

        if (lastCell is null || lastCell.Address.RowNumber < 2)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportNoData);
        }

        var lastRow = lastCell.Address.RowNumber;

        if (lastRow - 1 > MaxDataRows)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportTooManyRows);
        }

        var lastColumn = Math.Min(lastCell.Address.ColumnNumber, MaxColumns);

        return (lastRow, lastColumn);
    }

    public static string ReadCell(IXLWorksheet sheet, int row, int column, int maxLength = int.MaxValue)
    {
        var cell = sheet.Cell(row, column);

        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        string text;
        var value = cell.Value;

        if (value.IsNumber)
        {
            var number = value.GetNumber();
            text = number == Math.Floor(number) && Math.Abs(number) < 1e15
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : number.ToString("R", CultureInfo.InvariantCulture);
        }
        else if (value.IsBoolean)
        {
            text = value.GetBoolean() ? "true" : "false";
        }
        else
        {
            text = cell.GetFormattedString();
        }

        text = text.Trim();

        return text.Length > maxLength ? text[..maxLength] : text;
    }

    /// <summary>Ищет столбец по заголовку (без учёта регистра / пробелов).</summary>
    public static int? FindColumn(IReadOnlyList<string> headers, params string[] aliases)
    {
        for (var i = 0; i < headers.Count; i++)
        {
            var header = NormalizeHeader(headers[i]);
            foreach (var alias in aliases)
            {
                if (string.Equals(header, NormalizeHeader(alias), StringComparison.Ordinal))
                {
                    return i + 1; // 1-based для ClosedXML
                }
            }
        }

        return null;
    }

    public static string NormalizeHeader(string? value) =>
        (value ?? string.Empty).Trim().Replace(" ", string.Empty).ToLowerInvariant();

    /// <summary>
    /// Индекс с UI (с 0) → номер столбца Excel (с 1). null → null.
    /// Выброс ClientImportMappingRequired при выходе за границы.
    /// </summary>
    public static int? ToExcelColumn(int? zeroBasedIndex, int lastColumn)
    {
        if (zeroBasedIndex is null)
        {
            return null;
        }

        if (zeroBasedIndex.Value < 0 || zeroBasedIndex.Value >= lastColumn)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);
        }

        return zeroBasedIndex.Value + 1;
    }

    public static int RequireExcelColumn(int? zeroBasedIndex, int lastColumn) =>
        ToExcelColumn(zeroBasedIndex, lastColumn)
        ?? throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportMappingRequired);

    public static bool TryParseDecimal(string? raw, out decimal value)
    {
        value = 0m;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var normalized = raw.Trim().Replace(',', '.');
        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseBool(string? raw, bool defaultValue, out bool value)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            value = defaultValue;
            return true;
        }

        var normalized = raw.Trim().ToLowerInvariant();
        if (normalized is "1" or "true" or "yes" or "y" or "да" or "истина")
        {
            value = true;
            return true;
        }

        if (normalized is "0" or "false" or "no" or "n" or "нет" or "ложь")
        {
            value = false;
            return true;
        }

        value = defaultValue;
        return false;
    }

    private static readonly string[] DateFormats =
    [
        "yyyy-MM-dd", "dd.MM.yyyy", "dd/MM/yyyy", "d.M.yyyy", "d/M/yyyy",
        "MM/dd/yyyy", "M/d/yyyy", "yyyy/MM/dd", "dd-MM-yyyy", "d-M-yyyy"
    ];

    private static readonly string[] TimeFormats =
    [
        "HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss", "HH.mm", "H.mm"
    ];

    /// <summary>Пустая строка → null (успех). Иначе авто-разбор распространённых форматов.</summary>
    public static bool TryParseDateOnly(string? raw, out DateOnly? date)
    {
        date = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = raw.Trim();
        if (DateOnly.TryParseExact(text, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            date = exact;
            return true;
        }

        if (DateOnly.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out var ru)
            || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out ru)
            || DateOnly.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out ru))
        {
            date = ru;
            return true;
        }

        return false;
    }

    /// <summary>Пустая строка → null. Только 24-часовой формат (и Excel time fraction через OADate снаружи).</summary>
    public static bool TryParseTimeOnly(string? raw, out TimeOnly? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var text = raw.Trim();
        if (TimeOnly.TryParseExact(text, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exact))
        {
            time = new TimeOnly(exact.Hour, exact.Minute);
            return true;
        }

        if (TimeOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            || TimeOnly.TryParse(text, CultureInfo.GetCultureInfo("ru-RU"), DateTimeStyles.None, out parsed))
        {
            // Отсекаем 12-часовой AM/PM, если вдруг распарсился — требуем 24ч без am/pm в исходнике.
            if (text.Contains("am", StringComparison.OrdinalIgnoreCase)
                || text.Contains("pm", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            time = new TimeOnly(parsed.Hour, parsed.Minute);
            return true;
        }

        return false;
    }

    private static bool IsLegacyXls(byte[] data) =>
        data.Length >= 8
        && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0
        && data[4] == 0xA1 && data[5] == 0xB1 && data[6] == 0x1A && data[7] == 0xE1;

    private static bool HasZipSignature(byte[] data) =>
        data.Length >= 4
        && data[0] == 0x50 && data[1] == 0x4B
        && ((data[2] == 0x03 && data[3] == 0x04) || (data[2] == 0x05 && data[3] == 0x06));

    private static void ValidateZipStructure(byte[] data)
    {
        try
        {
            using var stream = new MemoryStream(data, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            if (archive.Entries.Count == 0 || archive.Entries.Count > MaxZipEntries)
            {
                throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
            }

            long totalUncompressed = 0;

            foreach (var entry in archive.Entries)
            {
                totalUncompressed += entry.Length;

                if (totalUncompressed > MaxUncompressedBytes)
                {
                    throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportFileTooLarge);
                }
            }

            if (archive.GetEntry("xl/workbook.xml") is null)
            {
                throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
            }
        }
        catch (InvalidDataException)
        {
            throw new SpreadsheetImportException(ApiErrorCodes.SpreadsheetImportInvalidFile);
        }
    }
}
