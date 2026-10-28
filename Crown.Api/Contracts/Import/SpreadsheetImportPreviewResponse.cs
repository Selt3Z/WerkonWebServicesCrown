namespace Crown.Contracts.Import;

public sealed class SpreadsheetImportPreviewResponse
{
    /// <summary>Заголовки столбцов (первая строка листа); индекс в списке = индекс столбца для маппинга.</summary>
    public List<string> Headers { get; set; } = [];

    /// <summary>До 20 строк данных (без строки заголовков).</summary>
    public List<List<string>> SampleRows { get; set; } = [];

    /// <summary>Сколько всего непустых строк данных в файле.</summary>
    public int TotalRows { get; set; }
}