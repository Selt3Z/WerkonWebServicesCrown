using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Crown.Infrastructure.Persistence;

namespace Crown.Infrastructure.Audit;

public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly AuditEntryFactory _auditEntryFactory;
    private List<Domain.Entities.AuditLogEntry>? _pendingEntries;
    private bool _suppressAudit;

    public AuditSaveChangesInterceptor(
        IHttpContextAccessor httpContextAccessor,
        AuditEntryFactory auditEntryFactory)
    {
        _httpContextAccessor = httpContextAccessor;
        _auditEntryFactory = auditEntryFactory;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (!_suppressAudit && eventData.Context is AppDbContext dbContext)
        {
            var trackedEntries = dbContext.ChangeTracker.Entries()
                .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            if (trackedEntries.Count > 0)
            {
                var (userId, userDisplayName) = await ResolveCurrentUserAsync(dbContext, cancellationToken);
                _pendingEntries = await _auditEntryFactory.CreateEntriesAsync(
                    dbContext,
                    trackedEntries,
                    userId,
                    userDisplayName,
                    cancellationToken);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        var savedCount = await base.SavedChangesAsync(eventData, result, cancellationToken);

        if (_suppressAudit
            || savedCount == 0
            || eventData.Context is not AppDbContext dbContext
            || _pendingEntries is not { Count: > 0 })
        {
            _pendingEntries = null;
            return savedCount;
        }

        _suppressAudit = true;

        try
        {
            var actorUserId = _pendingEntries
                .Select(x => x.UserId)
                .FirstOrDefault(id => id.HasValue);

            if (actorUserId.HasValue)
            {
                var actor = await dbContext.Users
                    .SingleOrDefaultAsync(x => x.Id == actorUserId.Value, cancellationToken);

                if (actor is not null)
                {
                    actor.LastActivityAtUtc = DateTime.UtcNow;
                }
            }

            dbContext.AuditLogEntries.AddRange(_pendingEntries);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _suppressAudit = false;
            _pendingEntries = null;
        }

        return savedCount;
    }

    private async Task<(Guid? UserId, string DisplayName)> ResolveCurrentUserAsync(
        AppDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        var principal = httpContext?.User;

        if (principal?.Identity?.IsAuthenticated != true)
        {
            return (null, "System");
        }

        Guid? userId = null;

        var userIdValue = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (Guid.TryParse(userIdValue, out var parsedUserId))
        {
            userId = parsedUserId;

            var displayName = await dbContext.Users
                .Where(x => x.Id == parsedUserId)
                .Select(x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.UserName! : x.DisplayName)
                .FirstOrDefaultAsync(cancellationToken);

            if (!string.IsNullOrWhiteSpace(displayName))
            {
                return (userId, displayName);
            }
        }

        var fallbackName = principal.Identity?.Name;

        return (userId, string.IsNullOrWhiteSpace(fallbackName) ? "System" : fallbackName);
    }
}
