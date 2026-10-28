using System.Globalization;
using Crown.Infrastructure.Settings;

namespace Crown.Infrastructure.Pdf;

public sealed class PdfDocumentLocale
{
    public string CultureName { get; init; } = "ru-RU";

    public string CurrencyCode { get; init; } = AppSettingsDefaults.CurrencyCode;

    public int CurrencyDecimalPlaces { get; init; } = AppSettingsDefaults.CurrencyDecimalPlaces;

    public CultureInfo Culture => CultureInfo.GetCultureInfo(PdfCultureResolver.Resolve(CultureName));

    public string T(string key) => PdfStrings.Get(CultureName, key);

    public string FormatMoney(decimal amount) =>
        PdfMoneyFormatter.Format(amount, CultureName, CurrencyCode, CurrencyDecimalPlaces);

    public static PdfDocumentLocale Create(string? cultureName, string currencyCode)
    {
        var normalizedCurrency = string.IsNullOrWhiteSpace(currencyCode)
            ? AppSettingsDefaults.CurrencyCode
            : currencyCode.Trim().ToUpperInvariant();

        return new PdfDocumentLocale
        {
            CultureName = PdfCultureResolver.Resolve(cultureName),
            CurrencyCode = normalizedCurrency,
            CurrencyDecimalPlaces = string.Equals(normalizedCurrency, "JPY", StringComparison.OrdinalIgnoreCase)
                ? 0
                : AppSettingsDefaults.CurrencyDecimalPlaces
        };
    }
}
