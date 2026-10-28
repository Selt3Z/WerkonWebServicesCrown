namespace Crown.Infrastructure.Pdf;

public static class PdfCultureResolver
{
    public static string Resolve(string? culture) =>
        culture?.Trim().ToLowerInvariant() switch
        {
            "ru" or "ru-ru" => "ru-RU",
            "en" or "en-us" => "en-US",
            "ja" or "ja-jp" => "ja-JP",
            _ => "ru-RU"
        };
}
