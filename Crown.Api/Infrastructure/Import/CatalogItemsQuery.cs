namespace Crown.Infrastructure.Import;

/// <summary>Общий фильтр/пейджинг для справочников act/work items.</summary>
public static class CatalogItemsQuery
{
    public const int DefaultTake = 50;
    public const int MaxTake = 200;

    public static IQueryable<T> ApplyTextFilter<T>(IQueryable<T> q, string? search, Func<IQueryable<T>, string, IQueryable<T>> contains)
    {
        var term = (search ?? string.Empty).Trim();
        if (term.Length == 0)
        {
            return q;
        }

        return contains(q, term);
    }

    public static (int Take, int Skip) ClampPage(int? take, int? skip)
    {
        var t = take is int tv && tv > 0 ? Math.Min(tv, MaxTake) : DefaultTake;
        var s = skip is int sv && sv > 0 ? sv : 0;
        return (t, s);
    }
}
