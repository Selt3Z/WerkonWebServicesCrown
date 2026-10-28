using TimeZoneNames;

namespace Crown.Web.Services;

/// <summary>Список и подписи TZ как в Ratchet (TimeZoneNames + системные зоны).</summary>
public static class TimeZoneDisplayHelper
{
    public static string FormatLabel(TimeZoneInfo timeZone, string languageCode = "ru")
    {
        var language = NormalizeLanguage(languageCode);
        var name = TZNames.GetDisplayNameForTimeZone(timeZone.Id, language);

        if (!string.IsNullOrWhiteSpace(name))
        {
            return name;
        }

        var names = TZNames.GetNamesForTimeZone(timeZone.Id, language);
        var fallbackName = !string.IsNullOrWhiteSpace(names.Generic)
            ? names.Generic
            : (!string.IsNullOrWhiteSpace(names.Standard) ? names.Standard : timeZone.Id);

        return $"{FormatOffset(timeZone.BaseUtcOffset)} {fallbackName}";
    }

    public static IReadOnlyList<TimeZoneInfo> GetOrderedTimeZones(string? selectedTimeZoneId = null)
    {
        const string language = "ru";

        var list = TimeZoneInfo.GetSystemTimeZones()
            .Select(tz => (TimeZone: tz, Label: FormatLabel(tz, language)))
            .GroupBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => PickPreferred(group, selectedTimeZoneId))
            .OrderBy(x => x.TimeZone.BaseUtcOffset)
            .ThenBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => x.TimeZone)
            .ToList();

        // Если выбранный IANA-id не попал в системный список — всё равно показываем опцию.
        if (!string.IsNullOrWhiteSpace(selectedTimeZoneId)
            && list.All(x => !string.Equals(x.Id, selectedTimeZoneId, StringComparison.OrdinalIgnoreCase))
            && TimeZoneInfo.TryFindSystemTimeZoneById(selectedTimeZoneId.Trim(), out var selected))
        {
            list.Insert(0, selected);
        }

        return list;
    }

    private static (TimeZoneInfo TimeZone, string Label) PickPreferred(
        IEnumerable<(TimeZoneInfo TimeZone, string Label)> group,
        string? selectedTimeZoneId)
    {
        var items = group.ToList();

        if (!string.IsNullOrWhiteSpace(selectedTimeZoneId))
        {
            var selected = items.FirstOrDefault(x =>
                string.Equals(x.TimeZone.Id, selectedTimeZoneId, StringComparison.OrdinalIgnoreCase));

            if (selected.TimeZone is not null)
            {
                return selected;
            }
        }

        return items
            .OrderBy(x => CountPathSegments(x.TimeZone.Id))
            .ThenBy(x => x.TimeZone.Id.Length)
            .ThenBy(x => x.TimeZone.Id, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    private static int CountPathSegments(string timeZoneId) =>
        timeZoneId.Count(c => c == '/');

    private static string NormalizeLanguage(string languageCode) =>
        languageCode?.Trim().ToLowerInvariant() switch
        {
            "ru" => "ru",
            "ja" => "ja",
            _ => "en"
        };

    private static string FormatOffset(TimeSpan offset)
    {
        var sign = offset < TimeSpan.Zero ? "-" : "+";
        var absolute = offset.Duration();
        return $"(UTC{sign}{absolute.Hours:00}:{absolute.Minutes:00})";
    }
}
