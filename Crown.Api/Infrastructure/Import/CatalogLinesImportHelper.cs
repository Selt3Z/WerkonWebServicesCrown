using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using ExcelDataReader;

namespace Crown.Infrastructure.Import;

/// <summary>
/// Разбор справочника (пункты акта / работы) из xlsx/xls/csv/txt/docx/doc.
/// Берём первый текстовый столбец таблицы или абзацы/строки документа.
/// </summary>
public static class CatalogLinesImportHelper
{
    public const int MaxFileBytes = 20 * 1024 * 1024;
    public const int MaxLines = 20_000;
    public const int MinLineLength = 2;

    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".xlsx", ".xls", ".csv", ".txt", ".docx", ".doc"
    };

    private static readonly Regex Ws = new(@"\s+", RegexOptions.Compiled);

    public static List<string> Parse(Stream stream, string? fileName)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        if (data.Length == 0)
        {
            throw new SpreadsheetImportException("catalog_import_empty", "Файл пуст.");
        }

        if (data.Length > MaxFileBytes)
        {
            throw new SpreadsheetImportException(
                "catalog_import_too_large",
                $"Файл больше {MaxFileBytes / (1024 * 1024)} МБ.");
        }

        var ext = Path.GetExtension(fileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExt.Contains(ext))
        {
            throw new SpreadsheetImportException(
                "catalog_import_unsupported",
                "Поддерживаются: xlsx, xls, csv, txt, doc, docx.");
        }

        var raw = ext.ToLowerInvariant() switch
        {
            ".txt" or ".csv" => ParseTextLines(data),
            ".xlsx" => ParseXlsx(data),
            ".xls" => ParseViaExcelDataReader(data),
            ".docx" => ParseDocx(data),
            ".doc" => ParseDocBinary(data),
            _ => throw new SpreadsheetImportException(
                "catalog_import_unsupported",
                "Поддерживаются: xlsx, xls, csv, txt, doc, docx.")
        };

        return NormalizeUnique(raw);
    }

    public static string NormKey(string s)
    {
        var t = Ws.Replace(s.Trim(), " ");
        return t.Replace('ё', 'е').Replace('Ё', 'Е').ToLowerInvariant();
    }

    private static List<string> NormalizeUnique(IEnumerable<string> raw)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in raw)
        {
            if (result.Count >= MaxLines)
            {
                break;
            }

            var t = Ws.Replace((line ?? string.Empty).Trim(), " ");
            if (t.Length < MinLineLength)
            {
                continue;
            }

            var key = NormKey(t);
            if (!seen.Add(key))
            {
                continue;
            }

            result.Add(t);
        }

        return result;
    }

    private static IEnumerable<string> ParseTextLines(byte[] data)
    {
        // UTF-8 с BOM / fallback CP1251 для старых выгрузок.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        string text;
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(data, 3, data.Length - 3);
        }
        else
        {
            try
            {
                text = Encoding.UTF8.GetString(data);
                // если много «» — скорее win-1251
                if (text.Count(c => c == '\uFFFD') > 2)
                {
                    text = Encoding.GetEncoding(1251).GetString(data);
                }
            }
            catch
            {
                text = Encoding.GetEncoding(1251).GetString(data);
            }
        }

        using var reader = new StringReader(text);
        string? line;
        var first = true;
        while ((line = reader.ReadLine()) is not null)
        {
            var t = line.Trim().Trim('"');
            // csv: берём первую колонку
            if (t.Contains(';') || t.Contains(','))
            {
                var sep = t.Count(c => c == ';') >= t.Count(c => c == ',') ? ';' : ',';
                t = t.Split(sep)[0].Trim().Trim('"');
            }

            if (first && IsHeader(t))
            {
                first = false;
                continue;
            }

            first = false;
            if (!string.IsNullOrWhiteSpace(t))
            {
                yield return t;
            }
        }
    }

    private static IEnumerable<string> ParseXlsx(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var wb = new XLWorkbook(ms);
        var sheet = wb.Worksheets.First();
        var used = sheet.RangeUsed();
        if (used is null)
        {
            yield break;
        }

        var rows = used.RowsUsed().ToList();
        if (rows.Count == 0)
        {
            yield break;
        }

        var col = PickTextColumn(rows[0]);
        var start = IsHeader(CellText(rows[0].Cell(col))) ? 1 : 0;
        for (var i = start; i < rows.Count; i++)
        {
            var t = CellText(rows[i].Cell(col));
            if (!string.IsNullOrWhiteSpace(t))
            {
                yield return t;
            }
        }
    }

    private static int PickTextColumn(IXLRangeRow headerRow)
    {
        var last = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 1;
        for (var c = 1; c <= last; c++)
        {
            var h = CellText(headerRow.Cell(c));
            if (IsPreferredHeader(h))
            {
                return c;
            }
        }

        return 1;
    }

    private static bool IsPreferredHeader(string h) =>
        h.Contains("нарушен", StringComparison.OrdinalIgnoreCase)
        || h.Contains("пункт", StringComparison.OrdinalIgnoreCase)
        || h.Contains("текст", StringComparison.OrdinalIgnoreCase)
        || h.Contains("работ", StringComparison.OrdinalIgnoreCase)
        || h.Contains("замеч", StringComparison.OrdinalIgnoreCase);

    private static bool IsHeader(string t) =>
        IsPreferredHeader(t)
        || string.Equals(t, "название", StringComparison.OrdinalIgnoreCase)
        || string.Equals(t, "name", StringComparison.OrdinalIgnoreCase);

    private static string CellText(IXLCell cell)
    {
        if (cell.IsEmpty())
        {
            return string.Empty;
        }

        return cell.GetFormattedString()?.Trim() ?? string.Empty;
    }

    private static IEnumerable<string> ParseViaExcelDataReader(byte[] data)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var ms = new MemoryStream(data);
        using var reader = ExcelReaderFactory.CreateReader(ms);
        var first = true;
        var col = 0;
        do
        {
            while (reader.Read())
            {
                if (reader.FieldCount == 0)
                {
                    continue;
                }

                if (first)
                {
                    for (var i = 0; i < reader.FieldCount; i++)
                    {
                        var h = Convert.ToString(reader.GetValue(i))?.Trim() ?? string.Empty;
                        if (IsPreferredHeader(h))
                        {
                            col = i;
                            break;
                        }
                    }

                    var head = Convert.ToString(reader.GetValue(col))?.Trim() ?? string.Empty;
                    first = false;
                    if (IsHeader(head))
                    {
                        continue;
                    }
                }

                var t = Convert.ToString(reader.GetValue(col))?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(t))
                {
                    yield return t;
                }
            }
        } while (reader.NextResult());
    }

    private static IEnumerable<string> ParseDocx(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
        var entry = zip.GetEntry("word/document.xml")
            ?? throw new SpreadsheetImportException("catalog_import_invalid_docx", "В docx нет word/document.xml.");

        using var entryStream = entry.Open();
        XDocument doc;
        try
        {
            doc = XDocument.Load(entryStream);
        }
        catch
        {
            throw new SpreadsheetImportException("catalog_import_invalid_docx", "Не удалось прочитать docx.");
        }

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        foreach (var p in doc.Descendants(w + "p"))
        {
            var texts = p.Descendants(w + "t").Select(t => t.Value);
            var line = string.Concat(texts).Trim();
            if (!string.IsNullOrWhiteSpace(line))
            {
                yield return line;
            }
        }
    }

    /// <summary>
    /// Грубий разбор legacy .doc: вытаскиваем UTF-16LE текстовые прогоны.
    /// Для сложных OLE/встроенных объектов может быть неполно — лучше docx/xlsx.
    /// </summary>
    private static IEnumerable<string> ParseDocBinary(byte[] data)
    {
        // OLE compound обычно начинается с D0 CF 11 E0
        if (data.Length < 512)
        {
            throw new SpreadsheetImportException("catalog_import_invalid_doc", "Файл .doc слишком короткий.");
        }

        var sb = new StringBuilder();
        var chunks = new List<string>();
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var ch = (char)(data[i] | (data[i + 1] << 8));
            if (IsDocChar(ch))
            {
                sb.Append(ch);
                continue;
            }

            if (sb.Length >= MinLineLength)
            {
                chunks.Add(sb.ToString());
            }

            sb.Clear();
        }

        if (sb.Length >= MinLineLength)
        {
            chunks.Add(sb.ToString());
        }

        // режем по переводам / очень длинные куски по «. » не трогаем — одна строка = один прогон
        var any = false;
        foreach (var chunk in chunks)
        {
            foreach (var line in chunk.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var t = line.Trim();
                // отсекаем мусор OLE-имён
                if (t.Length < 4)
                {
                    continue;
                }

                if (t.StartsWith("Root Entry", StringComparison.OrdinalIgnoreCase)
                    || t.Contains("WordDocument", StringComparison.Ordinal)
                    || t.Contains("SummaryInformation", StringComparison.Ordinal))
                {
                    continue;
                }

                any = true;
                yield return t;
            }
        }

        if (!any)
        {
            throw new SpreadsheetImportException(
                "catalog_import_invalid_doc",
                "Не удалось извлечь текст из .doc. Сохраните как .docx или .txt.");
        }
    }

    private static bool IsDocChar(char ch) =>
        ch is '\t' or '\n' or '\r'
        || (ch >= 32 && ch != 0x7F)
        || char.IsLetterOrDigit(ch)
        || char.IsPunctuation(ch)
        || char.IsSymbol(ch)
        || char.IsWhiteSpace(ch);
}
