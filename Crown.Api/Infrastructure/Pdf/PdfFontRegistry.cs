using System.Globalization;
using System.Reflection;
using QuestPDF.Drawing;

namespace Crown.Infrastructure.Pdf;

public static class PdfFontRegistry
{
    public const string SansFamily = "Noto Sans";
    public const string JapaneseFamily = "Noto Sans JP";

    // Имя ресурса = RootNamespace csproj (Crown.Api) + путь файла.
    private const string SansResource = "Crown.Api.Infrastructure.Pdf.Fonts.NotoSans-Regular.ttf";
    private const string JapaneseResource = "Crown.Api.Infrastructure.Pdf.Fonts.NotoSansJP-Regular.otf";

    private static int _initialized;

    public static void EnsureInitialized()
    {
        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
        {
            return;
        }

        var assembly = typeof(PdfFontRegistry).Assembly;
        RegisterFont(assembly, SansResource, SansFamily);
        RegisterFont(assembly, JapaneseResource, JapaneseFamily);
    }

    public static (string Primary, string Fallback) GetFontPair(string? cultureName)
    {
        EnsureInitialized();

        var language = CultureInfo.GetCultureInfo(PdfCultureResolver.Resolve(cultureName)).TwoLetterISOLanguageName;
        return language == "ja"
            ? (JapaneseFamily, SansFamily)
            : (SansFamily, JapaneseFamily);
    }

    private static void RegisterFont(Assembly assembly, string resourceName, string fontFamily)
    {
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"PDF font resource not found: {resourceName}");

        FontManager.RegisterFontWithCustomName(fontFamily, stream);
    }
}
