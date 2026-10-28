using System.Globalization;

namespace Crown.Infrastructure.Pdf;

public static class PdfStrings
{
    private static readonly IReadOnlyDictionary<string, string> Russian = new Dictionary<string, string>
    {
        ["Pdf_Date"] = "Дата:",
        ["Pdf_Number"] = "№",
        ["Pdf_Period"] = "Период:",
        ["Pdf_ItemName"] = "Наименование",
        ["Pdf_Qty"] = "Кол-во",
        ["Pdf_UnitPrice"] = "Цена",
        ["Pdf_Amount"] = "Сумма",
        ["Pdf_GeneratedAt"] = "сформировано",
        ["Pdf_Phone"] = "тел.",
        ["Pdf_Inn"] = "ИНН",
        ["Pdf_Kpp"] = "КПП",
        ["Pdf_Ogrn"] = "ОГРН",
        ["Pdf_Empty"] = "—",




        ["Inventory_Title"] = "ИНВЕНТАРНАЯ ВЕДОМОСТЬ",
        ["Inventory_Code"] = "Код",
        ["Inventory_Barcode"] = "Штрихкод",
        ["Inventory_Category"] = "Категория",
        ["Inventory_Unit"] = "Ед.",
        ["Inventory_Qty"] = "Остаток",
        ["Inventory_MinQty"] = "Мин.",
        ["Inventory_Archived"] = "архив",
        ["Inventory_Empty"] = "Нет позиций по выбранным фильтрам.",
        ["Inventory_TotalRows"] = "Всего позиций",
        ["Inventory_ShortageOnly"] = "только дефицит",
        ["Inventory_IncludeArchived"] = "включая архив"
    };

    private static readonly IReadOnlyDictionary<string, string> English = new Dictionary<string, string>
    {
        ["Pdf_Date"] = "Date:",
        ["Pdf_Number"] = "No.",
        ["Pdf_Period"] = "Period:",
        ["Pdf_ItemName"] = "Description",
        ["Pdf_Qty"] = "Qty",
        ["Pdf_UnitPrice"] = "Price",
        ["Pdf_Amount"] = "Amount",
        ["Pdf_GeneratedAt"] = "generated",
        ["Pdf_Phone"] = "tel.",
        ["Pdf_Inn"] = "TIN",
        ["Pdf_Kpp"] = "KPP",
        ["Pdf_Ogrn"] = "OGRN",
        ["Pdf_Empty"] = "—",




        ["Inventory_Title"] = "WAREHOUSE INVENTORY",
        ["Inventory_Code"] = "Code",
        ["Inventory_Barcode"] = "Barcode",
        ["Inventory_Category"] = "Category",
        ["Inventory_Unit"] = "Unit",
        ["Inventory_Qty"] = "Qty",
        ["Inventory_MinQty"] = "Min",
        ["Inventory_Archived"] = "archived",
        ["Inventory_Empty"] = "No parts match the selected filters.",
        ["Inventory_TotalRows"] = "Total rows",
        ["Inventory_ShortageOnly"] = "shortage only",
        ["Inventory_IncludeArchived"] = "include archived"
    };

    private static readonly IReadOnlyDictionary<string, string> Japanese = new Dictionary<string, string>
    {
        ["Pdf_Date"] = "日付:",
        ["Pdf_Number"] = "No.",
        ["Pdf_Period"] = "期間:",
        ["Pdf_ItemName"] = "項目",
        ["Pdf_Qty"] = "数量",
        ["Pdf_UnitPrice"] = "単価",
        ["Pdf_Amount"] = "金額",
        ["Pdf_GeneratedAt"] = "作成",
        ["Pdf_Phone"] = "TEL",
        ["Pdf_Inn"] = "法人番号",
        ["Pdf_Kpp"] = "KPP",
        ["Pdf_Ogrn"] = "登録番号",
        ["Pdf_Empty"] = "—",




        ["Inventory_Title"] = "倉庫在庫一覧",
        ["Inventory_Code"] = "コード",
        ["Inventory_Barcode"] = "バーコード",
        ["Inventory_Category"] = "カテゴリ",
        ["Inventory_Unit"] = "単位",
        ["Inventory_Qty"] = "在庫",
        ["Inventory_MinQty"] = "最小",
        ["Inventory_Archived"] = "アーカイブ",
        ["Inventory_Empty"] = "選択した条件に一致する部品はありません。",
        ["Inventory_TotalRows"] = "件数",
        ["Inventory_ShortageOnly"] = "不足のみ",
        ["Inventory_IncludeArchived"] = "アーカイブ含む"
    };

    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Strings;

    static PdfStrings()
    {
        Strings = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ru"] = Russian,
            ["en"] = English,
            ["ja"] = Japanese
        };
    }

    public static string Get(string cultureName, string key)
    {
        var language = CultureInfo.GetCultureInfo(PdfCultureResolver.Resolve(cultureName)).TwoLetterISOLanguageName;
        if (Strings.TryGetValue(language, out var table)
            && table is not null
            && table.TryGetValue(key, out var value))
        {
            return value;
        }

        return Russian.TryGetValue(key, out var fallback) ? fallback : key;
    }
}
