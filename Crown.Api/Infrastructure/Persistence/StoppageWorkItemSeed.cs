using Crown.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Crown.Infrastructure.Persistence;

/// <summary>Одноразово наполняет справочник работ из уже сохранённых остановок и привязывает их.</summary>
public static class StoppageWorkItemSeed
{
    public static async Task SeedFromExistingStoppagesAsync(
        WebApplication app,
        CancellationToken cancellationToken = default)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("StoppageWorkItemSeed");

        if (!db.Database.IsRelational())
        {
            return;
        }

        // Уже есть WorkItemId — не трогаем; берём уникальные тексты без привязки.
        var distinctTexts = await db.BrigadeStoppages.AsNoTracking()
            .Where(x => x.WorkItemId == null
                        && x.WorkDescription != null
                        && x.WorkDescription != "")
            .Select(x => x.WorkDescription)
            .ToListAsync(cancellationToken);

        var normalizedNew = distinctTexts
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && t.Length <= 2000)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        if (normalizedNew.Count == 0)
        {
            // Даже если новых текстов нет — могли добавить справочник вручную; всё равно долинкуем по тексту.
            await LinkStoppagesToExistingWorkItemsAsync(db, logger, cancellationToken);
            return;
        }

        var existing = await db.StoppageWorkItems.AsNoTracking().ToListAsync(cancellationToken);
        var byLower = existing.ToDictionary(x => x.Text.Trim().ToLowerInvariant(), x => x, StringComparer.OrdinalIgnoreCase);
        var maxOrder = existing.Count == 0 ? 0 : existing.Max(x => x.SortOrder);
        var added = 0;

        foreach (var text in normalizedNew.OrderBy(t => t, StringComparer.OrdinalIgnoreCase))
        {
            var key = text.ToLowerInvariant();
            if (byLower.ContainsKey(key))
            {
                continue;
            }

            maxOrder++;
            var entity = new StoppageWorkItem
            {
                Id = Guid.NewGuid(),
                Text = text.Length > 2000 ? text[..2000] : text,
                IsActive = true,
                SortOrder = maxOrder,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.StoppageWorkItems.Add(entity);
            byLower[key] = entity;
            added++;
        }

        if (added > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Seeded {Count} stoppage work item(s) from existing stoppages.", added);
        }

        await LinkStoppagesToExistingWorkItemsAsync(db, logger, cancellationToken);
    }

    private static async Task LinkStoppagesToExistingWorkItemsAsync(
        AppDbContext db,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var catalog = await db.StoppageWorkItems.AsNoTracking().ToListAsync(cancellationToken);
        if (catalog.Count == 0)
        {
            return;
        }

        var map = catalog
            .GroupBy(x => x.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key.ToLowerInvariant(), g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var unlinked = await db.BrigadeStoppages
            .Where(x => x.WorkItemId == null
                        && x.WorkDescription != null
                        && x.WorkDescription != "")
            .ToListAsync(cancellationToken);

        var linked = 0;
        foreach (var s in unlinked)
        {
            var key = s.WorkDescription.Trim().ToLowerInvariant();
            if (!map.TryGetValue(key, out var workId))
            {
                continue;
            }

            s.WorkItemId = workId;
            s.IsWorkManual = false;
            linked++;
        }

        if (linked > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation("Linked {Count} stoppage(s) to work catalog items.", linked);
        }
    }
}
