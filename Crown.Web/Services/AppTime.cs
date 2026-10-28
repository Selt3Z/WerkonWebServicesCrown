using System.Globalization;

namespace Crown.Web.Services;

/// <summary>Показ и разбор дат в часовом поясе приложения (AppTimeZone), а не сервера/браузера.</summary>
public sealed class AppTime
{
    private static readonly CultureInfo Ru = new("ru-RU");
    private static readonly string[] InputFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"];

    private readonly TimeZoneInfo _zone;

    public AppTime(IConfiguration configuration, ILogger<AppTime> logger)
    {
        var id = configuration["AppTimeZone"];
        try
        {
            _zone = string.IsNullOrWhiteSpace(id) ? TimeZoneInfo.Utc : TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // В минимальных образах может не быть tzdata — не падаем, берём МСК без переходов на летнее время.
            logger.LogWarning(ex, "Часовой пояс {Zone} не найден, используется UTC+3", id);
            _zone = TimeZoneInfo.CreateCustomTimeZone("MSK", TimeSpan.FromHours(3), "MSK", "MSK");
        }
    }

    public DateTimeOffset Now => ToLocal(DateTimeOffset.UtcNow);

    public DateTimeOffset ToLocal(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, _zone);

    public string Format(DateTimeOffset? value) =>
        value is null ? "—" : ToLocal(value.Value).ToString("dd.MM.yyyy HH:mm", Ru);

    /// <summary>Для DateTime из API (поля *Utc, Kind может быть Unspecified).</summary>
    public string FormatUtc(DateTime? value) =>
        value is null
            ? "—"
            : Format(new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc)));

    /// <summary>Значение для &lt;input type="datetime-local"&gt;.</summary>
    public string ToInput(DateTimeOffset value) =>
        ToLocal(value).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    public bool TryParseInput(string? text, out DateTimeOffset result)
    {
        result = default;
        if (!DateTime.TryParseExact(text, InputFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return false;
        }

        // Введено в поясе приложения -> переводим в offset этого пояса.
        result = new DateTimeOffset(local, _zone.GetUtcOffset(local));
        return true;
    }
}
