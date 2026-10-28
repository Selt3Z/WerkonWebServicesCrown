using System.Text;
using Crown.Web.Models;

namespace Crown.Web.Services;

public sealed record PagerModel(int Page, bool HasMore, Func<int, string> UrlFor);

public static class UiHelpers
{
    public const int PageSize = 50;

    public static string StatusClass(ServiceRequestStatus status) => status switch
    {
        ServiceRequestStatus.Submitted => "st-submitted",
        ServiceRequestStatus.InProgress => "st-progress",
        ServiceRequestStatus.Rejected => "st-rejected",
        ServiceRequestStatus.Returned => "st-returned",
        ServiceRequestStatus.Completed => "st-completed",
        ServiceRequestStatus.Cancelled => "st-cancelled",
        _ => string.Empty
    };

    /// <summary>Отображение нормализованного +7… как +7 (912) 345-67-89.</summary>
    public static string FormatPhone(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return "—";
        }

        var digits = new StringBuilder(stored.Length);
        foreach (var c in stored)
        {
            if (char.IsDigit(c))
            {
                digits.Append(c);
            }
        }

        var d = digits.ToString();
        if (d.Length == 11 && d[0] == '7')
        {
            return $"+7 ({d[1..4]}) {d[4..7]}-{d[7..9]}-{d[9..11]}";
        }

        return stored.Trim();
    }

    /// <summary>Следующее направление при клике по заголовку: та же колонка → toggle, новая → asc (id → desc).</summary>
    public static string NextSortDir(string column, string? currentSort, string? currentDir)
    {
        var same = string.Equals(currentSort, column, StringComparison.OrdinalIgnoreCase);
        if (!same)
        {
            return string.Equals(column, "id", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
        }

        return string.Equals(currentDir, "asc", StringComparison.OrdinalIgnoreCase) ? "desc" : "asc";
    }

    public static string SortMark(string column, string? currentSort, string? currentDir)
    {
        if (!string.Equals(currentSort, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Equals(currentDir, "asc", StringComparison.OrdinalIgnoreCase) ? " ↑" : " ↓";
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double size = bytes;
        var i = 0;
        while (size >= 1024 && i < units.Length - 1)
        {
            size /= 1024;
            i++;
        }

        return i == 0 ? $"{bytes} {units[0]}" : $"{size:0.#} {units[i]}";
    }

    /// <summary>Строит URL списка: переданные пары, пустые значения пропускаются.</summary>
    public static string BuildUrl(string basePath, params (string Key, string? Value)[] query)
    {
        var parts = query
            .Where(q => !string.IsNullOrEmpty(q.Value))
            .Select(q => $"{Uri.EscapeDataString(q.Key)}={Uri.EscapeDataString(q.Value!)}");
        var qs = string.Join("&", parts);
        return qs.Length == 0 ? basePath : basePath + "?" + qs;
    }
}
