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
