namespace Crown.Infrastructure.Import;

/// <summary>Ошибка импорта таблицы (каталог/склад); Code — один из ApiErrorCodes.</summary>
public sealed class SpreadsheetImportException : Exception
{
    public SpreadsheetImportException(string code)
        : base(code)
    {
        Code = code;
    }

    public string Code { get; }
}