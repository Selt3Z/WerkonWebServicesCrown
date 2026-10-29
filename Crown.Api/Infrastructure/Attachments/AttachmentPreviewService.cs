using System.Data;
using System.Text;
using ClosedXML.Excel;
using Crown.Contracts.Requests;
using ExcelDataReader;
using SharpCompress.Archives;

namespace Crown.Infrastructure.Attachments;

/// <summary>Разбор вложения для UI-превью: текст/excel/архив.</summary>
public sealed class AttachmentPreviewService
{
    private const int MaxTextChars = 400_000;
    private const int MaxTableRows = 200;
    private const int MaxTableCols = 40;
    private const int MaxArchiveEntries = 2_000;

    static AttachmentPreviewService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public AttachmentPreviewResponse Build(string fileName, string contentType, Stream content)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var ct = (contentType ?? string.Empty).ToLowerInvariant();
        var result = new AttachmentPreviewResponse
        {
            FileName = fileName,
            ContentType = contentType ?? string.Empty
        };

        if (IsPdf(ext, ct))
        {
            result.Kind = "pdf";
            return result;
        }

        if (IsImage(ext, ct))
        {
            result.Kind = "image";
            return result;
        }

        if (IsText(ext, ct))
        {
            result.Kind = "text";
            result.Text = ReadText(content);
            return result;
        }

        if (IsExcel(ext, ct))
        {
            result.Kind = "table";
            try
            {
                result.Sheets = ReadExcel(content, ext);
            }
            catch (Exception ex)
            {
                result.Kind = "unsupported";
                result.Message = "Не удалось прочитать таблицу: " + ex.Message;
            }

            return result;
        }

        if (IsArchive(ext, ct))
        {
            result.Kind = "archive";
            try
            {
                result.Entries = ReadArchive(content, ext);
            }
            catch (Exception ex)
            {
                result.Kind = "unsupported";
                result.Message = "Не удалось прочитать архив: " + ex.Message;
            }

            return result;
        }

        result.Kind = "unsupported";
        result.Message = "Превью для этого типа файла недоступно — скачайте файл.";
        return result;
    }

    private static bool IsPdf(string ext, string ct) =>
        ext is ".pdf" || ct.Contains("pdf", StringComparison.Ordinal);

    private static bool IsImage(string ext, string ct) =>
        ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".svg"
        || ct.StartsWith("image/", StringComparison.Ordinal);

    private static bool IsText(string ext, string ct) =>
        ext is ".txt" or ".log" or ".csv" or ".tsv" or ".md" or ".json" or ".xml" or ".html" or ".htm" or ".css" or ".js" or ".cs" or ".sql"
        || ct.StartsWith("text/", StringComparison.Ordinal)
        || ct is "application/json" or "application/xml";

    private static bool IsExcel(string ext, string ct) =>
        ext is ".xlsx" or ".xlsm" or ".xls" or ".ods"
        || ct.Contains("spreadsheet", StringComparison.Ordinal)
        || ct.Contains("excel", StringComparison.Ordinal);

    private static bool IsArchive(string ext, string ct) =>
        ext is ".zip" or ".7z" or ".rar" or ".tar" or ".gz" or ".tgz" or ".bz2"
        || ct is "application/zip" or "application/x-zip-compressed" or "application/x-7z-compressed"
            or "application/x-rar-compressed" or "application/vnd.rar" or "application/x-tar"
            or "application/gzip";

    private static string ReadText(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var sb = new StringBuilder();
        var buffer = new char[4096];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            sb.Append(buffer, 0, read);
            if (sb.Length >= MaxTextChars)
            {
                sb.Length = MaxTextChars;
                sb.Append("\n…");
                break;
            }
        }

        return sb.ToString();
    }

    private static List<AttachmentPreviewSheet> ReadExcel(Stream content, string ext)
    {
        // xlsx/xlsm — ClosedXML; xls и прочее — ExcelDataReader.
        if (ext is ".xlsx" or ".xlsm")
        {
            using var book = new XLWorkbook(content);
            var sheets = new List<AttachmentPreviewSheet>();
            foreach (var ws in book.Worksheets)
            {
                var sheet = new AttachmentPreviewSheet { Name = ws.Name };
                var range = ws.RangeUsed();
                if (range is null)
                {
                    sheets.Add(sheet);
                    continue;
                }

                var rowCount = Math.Min(range.RowCount(), MaxTableRows);
                var colCount = Math.Min(range.ColumnCount(), MaxTableCols);
                for (var r = 1; r <= rowCount; r++)
                {
                    var row = new List<string>(colCount);
                    for (var c = 1; c <= colCount; c++)
                    {
                        row.Add(range.Cell(r, c).GetFormattedString());
                    }

                    sheet.Rows.Add(row);
                }

                sheets.Add(sheet);
                if (sheets.Count >= 10)
                {
                    break;
                }
            }

            return sheets;
        }

        using var reader = ExcelReaderFactory.CreateReader(content);
        var conf = new ExcelDataSetConfiguration
        {
            ConfigureDataTable = _ => new ExcelDataTableConfiguration { UseHeaderRow = false }
        };
        var data = reader.AsDataSet(conf);
        var result = new List<AttachmentPreviewSheet>();
        foreach (DataTable table in data.Tables)
        {
            var sheet = new AttachmentPreviewSheet { Name = table.TableName };
            var rows = Math.Min(table.Rows.Count, MaxTableRows);
            var cols = Math.Min(table.Columns.Count, MaxTableCols);
            for (var r = 0; r < rows; r++)
            {
                var row = new List<string>(cols);
                for (var c = 0; c < cols; c++)
                {
                    row.Add(Convert.ToString(table.Rows[r][c]) ?? string.Empty);
                }

                sheet.Rows.Add(row);
            }

            result.Add(sheet);
            if (result.Count >= 10)
            {
                break;
            }
        }

        return result;
    }

    private static List<AttachmentArchiveEntry> ReadArchive(Stream content, string ext)
    {
        // zip — стандартный ZipArchive; rar/7z/tar — SharpCompress.
        if (ext == ".zip")
        {
            using var zip = new System.IO.Compression.ZipArchive(content, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
            return zip.Entries
                .Take(MaxArchiveEntries)
                .Select(e => new AttachmentArchiveEntry
                {
                    Path = e.FullName.Replace('\\', '/'),
                    IsDirectory = string.IsNullOrEmpty(e.Name) || e.FullName.EndsWith('/'),
                    SizeBytes = string.IsNullOrEmpty(e.Name) ? null : e.Length
                })
                .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        content.Position = 0;
        var entries = new List<AttachmentArchiveEntry>();
        using (var archive = ArchiveFactory.Open(content))
        {
            foreach (var entry in archive.Entries.Take(MaxArchiveEntries))
            {
                entries.Add(new AttachmentArchiveEntry
                {
                    Path = (entry.Key ?? string.Empty).Replace('\\', '/'),
                    IsDirectory = entry.IsDirectory,
                    SizeBytes = entry.IsDirectory ? null : entry.Size
                });
            }
        }

        return entries
            .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
