using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;

namespace Crown.Infrastructure.Persistence;

public static class QueryPagingExtensions
{
    public const int DefaultTake = 30;
    public const int MaxTake = 500;

    public static (int Skip, int Take) NormalizePaging(int? skip, int? take) =>
        (Math.Max(skip ?? 0, 0), Math.Clamp(take ?? DefaultTake, 1, MaxTake));

    /// <summary>
    /// Loads one extra row beyond <paramref name="take"/> to detect whether more data exists
    /// without issuing a separate COUNT query. The query must have a stable ORDER BY.
    /// </summary>
    public static async Task<PagedResponse<T>> ToPagedResponseAsync<T>(
        this IQueryable<T> query,
        int skip,
        int take,
        CancellationToken cancellationToken)
    {
        var items = await query
            .Skip(skip)
            .Take(take + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > take;

        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new PagedResponse<T>
        {
            Items = items,
            HasMore = hasMore
        };
    }
}
