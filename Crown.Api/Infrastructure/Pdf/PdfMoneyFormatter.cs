using System.Globalization;
using Crown.Infrastructure.Settings;

namespace Crown.Infrastructure.Pdf;

public static class PdfMoneyFormatter
{
    public static string Format(
        decimal amount,
        string cultureName,
        string currencyCode,
        int? decimalPlaces = null)
    {
        var culture = CultureInfo.GetCultureInfo(PdfCultureResolver.Resolve(cultureName));
        var code = string.IsNullOrWhiteSpace(currencyCode)
            ? AppSettingsDefaults.CurrencyCode
            : currencyCode.Trim().ToUpperInvariant();
        var places = decimalPlaces ?? AppSettingsDefaults.CurrencyDecimalPlaces;
        var language = culture.TwoLetterISOLanguageName;

        if (code == "JPY")
        {
            places = 0;
        }

        var number = amount.ToString($"N{places}", culture);

        return (language, code) switch
        {
            ("ru", "RUB") => $"{number} руб.",
            ("ru", "USD") => $"${number}",
            ("ru", "EUR") => $"€{number}",
            ("ru", "JPY") => $"{number} йен",

            ("en", "USD") => $"${amount.ToString($"N{places}", CultureInfo.GetCultureInfo("en-US"))}",
            ("en", "RUB") => $"{amount.ToString($"N{places}", CultureInfo.GetCultureInfo("en-US"))} RUB",
            ("en", "EUR") => $"€{amount.ToString($"N{places}", CultureInfo.GetCultureInfo("en-US"))}",
            ("en", "JPY") => $"{amount.ToString("N0", CultureInfo.GetCultureInfo("en-US"))} JPY",

            ("ja", "RUB") => $"{number} ルーブル",
            ("ja", "USD") => $"{number} ドル",
            ("ja", "EUR") => $"{number} ユーロ",
            ("ja", "JPY") => $"{number} 円",

            _ => $"{number} {code}"
        };
    }
}
