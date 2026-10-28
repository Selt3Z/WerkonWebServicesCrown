namespace Crown.Domain;

public static class MeasurementUnits
{
    public const string Pieces = "шт";
    public const string Liter = "л";
    public const string Kilogram = "кг";
    public const string Meter = "м";
    public const string Set = "компл";
    public const string Pack = "уп";

    public static readonly IReadOnlyList<string> All =
    [
        Pieces,
        Liter,
        Kilogram,
        Meter,
        Set,
        Pack
    ];

    private static readonly HashSet<string> Allowed = new(All, StringComparer.Ordinal);

    private static readonly HashSet<string> IntegerOnly = new(StringComparer.Ordinal)
    {
        Pieces,
        Set,
        Pack
    };

    public static bool IsAllowed(string? unit) =>
        !string.IsNullOrWhiteSpace(unit) && Allowed.Contains(unit.Trim());

    public static bool RequiresWholeQuantity(string unit) => IntegerOnly.Contains(unit);

    public static int MaxDecimalPlaces(string unit) =>
        RequiresWholeQuantity(unit) ? 0 : 1;

    public static bool IsValidQuantity(string unit, decimal quantity)
    {
        if (quantity < 0)
        {
            return false;
        }

        if (RequiresWholeQuantity(unit))
        {
            return quantity == decimal.Truncate(quantity);
        }

        return quantity == decimal.Round(quantity, 1, MidpointRounding.AwayFromZero);
    }

    public static decimal NormalizeQuantity(string unit, decimal quantity) =>
        RequiresWholeQuantity(unit)
            ? decimal.Truncate(quantity)
            : decimal.Round(quantity, 1, MidpointRounding.AwayFromZero);
}
