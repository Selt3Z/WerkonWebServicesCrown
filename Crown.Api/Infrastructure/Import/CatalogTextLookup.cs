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

    public static async Task<StoppageActItem> FindOrCreateActAsync(
        AppDbContext db,
        string text,
        CancellationToken cancellationToken)
    {
        var trimmed = text.Trim();
        var key = NormKey(trimmed);
        var existing = await db.StoppageActItems
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
        var hit = existing.FirstOrDefault(x => NormKey(x.Text) == key);
        if (hit is not null)
        {
            if (!hit.IsActive)
            {
                hit.IsActive = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            return hit;
        }

        var maxOrder = existing.Count == 0 ? 0 : existing.Max(x => x.SortOrder);
        var entity = new StoppageActItem
        {
            Id = Guid.NewGuid(),
            Text = trimmed,
            IsActive = true,
            SortOrder = maxOrder + 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StoppageActItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public static async Task<StoppageWorkItem> FindOrCreateWorkAsync(
        AppDbContext db,
        string text,
        CancellationToken cancellationToken)
    {
        var trimmed = text.Trim();
        var key = NormKey(trimmed);
        var existing = await db.StoppageWorkItems
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.SortOrder)
            .ToListAsync(cancellationToken);
        var hit = existing.FirstOrDefault(x => NormKey(x.Text) == key);
        if (hit is not null)
        {
            if (!hit.IsActive)
            {
                hit.IsActive = true;
                await db.SaveChangesAsync(cancellationToken);
            }

            return hit;
        }

        var maxOrder = existing.Count == 0 ? 0 : existing.Max(x => x.SortOrder);
        var entity = new StoppageWorkItem
        {
            Id = Guid.NewGuid(),
            Text = trimmed,
            IsActive = true,
            SortOrder = maxOrder + 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StoppageWorkItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }
}
