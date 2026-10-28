namespace Crown.Infrastructure.Settings;

public static class AppThemeNames
{
    public const string Light = "light";

    public const string Dark = "dark";

    public const string Gray = "gray";

    public const string GrayRetro = "gray-retro";

    public const string Pink = "pink";

    public const string Green = "green";

    public const string GreenDark = "green-dark";

    public static string Normalize(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return Light;
        }

        return theme.Trim().ToLowerInvariant() switch
        {
            Dark => Dark,
            Gray => Gray,
            "grayretro" or GrayRetro => GrayRetro,
            Pink => Pink,
            Green => Green,
            "green-dark" or "dark-green" or GreenDark => GreenDark,
            _ => Light
        };
    }
}
