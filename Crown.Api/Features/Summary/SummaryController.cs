using System.Security.Claims;
using Crown.Contracts.Summary;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crown.Features.Summary;

[ApiController]
[Route("api/summary")]
[Authorize]
public sealed class SummaryController(
    SummaryReportService summary,
    UserManager<ApplicationUser> users,
    AppDbContext db,
    AppTimeZone time) : ControllerBase
{
    /// <summary>Год самой старой заявки (локальный TZ) — для селекта периода.</summary>
    [HttpGet("earliest-year")]
    public async Task<ActionResult<int>> EarliestYear(CancellationToken cancellationToken)
    {
        if (!await CanAccessSummaryAsync(cancellationToken))
        {
            return Forbid();
        }

        var minUtc = await db.ServiceRequests.AsNoTracking()
            .Select(x => (DateTimeOffset?)x.SubmittedAt)
            .MinAsync(cancellationToken);
        var todayYear = time.GetToday().Year;
        if (minUtc is null)
        {
            return Ok(todayYear);
        }

        var local = TimeZoneInfo.ConvertTime(minUtc.Value, time.TimeZoneInfo);
        return Ok(Math.Min(local.Year, todayYear));
    }

    [HttpGet]
    public async Task<ActionResult<SummaryResponse>> Get(
        [FromQuery] string? period = "today",
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] Guid? userId = null,
        CancellationToken cancellationToken = default)
    {
        var currentId = CurrentUserId();
        if (currentId is null)
        {
            return Unauthorized();
        }

        if (!await CanAccessSummaryAsync(CancellationToken.None))
        {
            return Forbid();
        }

        var canPickUser = User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);
        var targetId = userId is Guid id && id != Guid.Empty ? id : currentId.Value;

        if (targetId != currentId.Value && !canPickUser)
        {
            return Forbid();
        }

        var result = await summary.BuildAsync(
            targetId,
            period,
            year,
            month,
            includeUserPicker: canPickUser,
            cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private async Task<bool> CanAccessSummaryAsync(CancellationToken cancellationToken)
    {
        if (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher))
        {
            return true;
        }

        if (!User.IsInRole(AppRoles.Department))
        {
            return false;
        }

        var actor = await users.GetUserAsync(User);
        return actor is { CanViewSummary: true };
    }
}
