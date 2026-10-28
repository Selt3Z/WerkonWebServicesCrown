namespace Crown.Contracts.Import;

public sealed class SpreadsheetImportRowError
{
    /// <summary>Номер строки в Excel (с 1, как видит пользователь).</summary>
    public int RowNumber { get; set; }

    /// <summary>Код причины ошибки в строке (например, name_required).</summary>
    public string Code { get; set; } = string.Empty;
}