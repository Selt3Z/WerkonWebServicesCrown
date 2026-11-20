using System.Globalization;
using System.Text;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Crown.Infrastructure.Import;

/// <summary>Поиск/создание пунктов акта и выполняемых работ по тексту (без учёта регистра/ё).</summary>
public static class CatalogTextLookup
{
    public static string NormKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var s = text.Trim().Normalize(NormalizationForm.FormKC);
        var sb = new StringBuilder(s.Length);
        var prevSpace = false;
        foreach (var ch in s)
        {
            var c = ch is 'ё' or 'Ё' ? 'е' : ch;
            if (char.IsWhiteSpace(c))
            {
                if (prevSpace)
                {
                    continue;
                }

                sb.Append(' ');
                prevSpace = true;
                continue;
            }

            prevSpace = false;
            sb.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }

        return sb.ToString().Trim();
    }

    /// <summary>Лёгкий индекс справочника (AsNoTracking) — без 6k tracked-сущностей в ChangeTracker.</summary>
    public static async Task<ActCatalogIndex> LoadActIndexAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.StoppageActItems.AsNoTracking()
            .Select(x => new ActCatalogLite
            {
                Id = x.Id,
                Text = x.Text,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(x => x.Id);
        var byKey = new Dictionary<string, ActCatalogLite>(StringComparer.Ordinal);
        foreach (var a in rows)
        {
            var k = NormKey(a.Text);
            if (!string.IsNullOrEmpty(k) && !byKey.ContainsKey(k))
            {
                byKey[k] = a;
            }
        }

        return new ActCatalogIndex
        {
            ById = byId,
            ByKey = byKey,
            MaxSortOrder = rows.Count == 0 ? 0 : rows.Max(x => x.SortOrder)
        };
    }

    public static async Task<StoppageActItem> FindOrCreateActAsync(
        AppDbContext db,
        string text,
        CancellationToken cancellationToken)
    {
        var trimmed = text.Trim();
        var key = NormKey(trimmed);
        var index = await LoadActIndexAsync(db, cancellationToken);
        if (index.ByKey.TryGetValue(key, out var lite))
        {
            var tracked = await EnsureActActiveAsync(db, lite, cancellationToken);
            if (!lite.IsActive)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return tracked;
        }

        var entity = new StoppageActItem
        {
            Id = Guid.NewGuid(),
            Text = trimmed,
            IsActive = true,
            SortOrder = index.MaxSortOrder + 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StoppageActItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    /// <summary>Тот же хеш-индекс, что у акта — без линейного скана NormKey.</summary>
    public static async Task<WorkCatalogIndex> LoadWorkIndexAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.StoppageWorkItems.AsNoTracking()
            .Select(x => new WorkCatalogLite
            {
                Id = x.Id,
                Text = x.Text,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);

        var byId = rows.ToDictionary(x => x.Id);
        var byKey = new Dictionary<string, WorkCatalogLite>(StringComparer.Ordinal);
        foreach (var w in rows)
        {
            var k = NormKey(w.Text);
            if (!string.IsNullOrEmpty(k) && !byKey.ContainsKey(k))
            {
                byKey[k] = w;
            }
        }

        return new WorkCatalogIndex
        {
            ById = byId,
            ByKey = byKey,
            MaxSortOrder = rows.Count == 0 ? 0 : rows.Max(x => x.SortOrder)
        };
    }

    public static async Task<StoppageWorkItem> FindOrCreateWorkAsync(
        AppDbContext db,
        string text,
        CancellationToken cancellationToken)
    {
        var trimmed = text.Trim();
        var key = NormKey(trimmed);
        var index = await LoadWorkIndexAsync(db, cancellationToken);
        if (index.ByKey.TryGetValue(key, out var lite))
        {
            var tracked = await EnsureWorkActiveAsync(db, lite, cancellationToken);
            if (!lite.IsActive)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return tracked;
        }

        var entity = new StoppageWorkItem
        {
            Id = Guid.NewGuid(),
            Text = trimmed,
            IsActive = true,
            SortOrder = index.MaxSortOrder + 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StoppageWorkItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public static async Task<StoppageWorkItem> EnsureWorkActiveAsync(
        AppDbContext db,
        WorkCatalogLite lite,
        CancellationToken cancellationToken)
    {
        var tracked = await db.StoppageWorkItems.FirstAsync(x => x.Id == lite.Id, cancellationToken);
        if (!tracked.IsActive)
        {
            tracked.IsActive = true;
        }

        return tracked;
    }

    /// <summary>Подгружает tracked-сущность и помечает активной (SaveChanges — на вызывающем).</summary>
    public static async Task<StoppageActItem> EnsureActActiveAsync(
        AppDbContext db,
        ActCatalogLite lite,
        CancellationToken cancellationToken)
    {
        var tracked = await db.StoppageActItems.FirstAsync(x => x.Id == lite.Id, cancellationToken);
        if (!tracked.IsActive)
        {
            tracked.IsActive = true;
        }

        return tracked;
    }

    public sealed class ActCatalogLite
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class ActCatalogIndex
    {
        public Dictionary<Guid, ActCatalogLite> ById { get; init; } = new();
        public Dictionary<string, ActCatalogLite> ByKey { get; init; } = new(StringComparer.Ordinal);
        public int MaxSortOrder { get; set; }
    }

    public sealed class WorkCatalogLite
    {
        public Guid Id { get; set; }
        public string Text { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int SortOrder { get; set; }
    }

    public sealed class WorkCatalogIndex
    {
        public Dictionary<Guid, WorkCatalogLite> ById { get; init; } = new();
        public Dictionary<string, WorkCatalogLite> ByKey { get; init; } = new(StringComparer.Ordinal);
        public int MaxSortOrder { get; set; }
    }
}
