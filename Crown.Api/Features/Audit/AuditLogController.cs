using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Audit;
using Crown.Contracts.Common;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Time;

namespace Crown.Features.Audit;

[ApiController]
[Route("api/audit-log")]
[Authorize(Policy = AuthorizationPolicies.ViewAuditLog)]
public sealed class AuditLogController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly AppTimeZone _appTimeZone;

    public AuditLogController(AppDbContext dbContext, AppTimeZone appTimeZone)
    {
        _dbContext = dbContext;
        _appTimeZone = appTimeZone;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<AuditLogItemResponse>>> Get(
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromQuery] Guid? userId,
        [FromQuery] string? entityType,
        [FromQuery] DateOnly? dateFrom,
        [FromQuery] DateOnly? dateTo,
        CancellationToken cancellationToken)
    {
        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);

        var query = _dbContext.AuditLogEntries.AsNoTracking();

        if (userId.HasValue)
        {
            query = query.Where(x => x.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            var normalizedEntityType = entityType.Trim();
            query = query.Where(x => x.EntityType == normalizedEntityType);
        }

        if (dateFrom.HasValue)
        {
            var fromUtc = _appTimeZone.ToUtc(dateFrom.Value.ToDateTime(TimeOnly.MinValue));
            query = query.Where(x => x.OccurredAtUtc >= fromUtc);
        }

        if (dateTo.HasValue)
        {
            var toUtc = _appTimeZone.ToUtc(dateTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue));
            query = query.Where(x => x.OccurredAtUtc < toUtc);
        }

        var response = await query
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new AuditLogItemResponse
            {
                Id = x.Id,
                OccurredAtUtc = x.OccurredAtUtc,
                UserDisplayName = x.UserDisplayName,
                UserId = x.UserId,
                Action = x.Action,
                EntityType = x.EntityType,
                Summary = x.Summary,
                EntityUrl = x.EntityUrl
            })
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

    [HttpGet("users")]
    public async Task<ActionResult<List<AuditLogUserOptionResponse>>> GetUsers(
        CancellationToken cancellationToken)
    {
        var response = await _dbContext.AuditLogEntries
            .AsNoTracking()
            .Where(x => x.UserId.HasValue)
            .GroupBy(x => new { x.UserId, x.UserDisplayName })
            .Select(x => new AuditLogUserOptionResponse
            {
                UserId = x.Key.UserId!.Value,
                DisplayName = x.Key.UserDisplayName
            })
            .OrderBy(x => x.DisplayName)
            .ToListAsync(cancellationToken);

        return Ok(response);
    }
}
