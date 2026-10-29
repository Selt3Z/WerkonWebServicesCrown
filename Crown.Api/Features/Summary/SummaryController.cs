using System.Security.Claims;
using Crown.Contracts.Summary;
using Crown.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Crown.Features.Summary;

[ApiController]
[Route("api/summary")]
[Authorize(Roles = $"{AppRoles.Administrator},{AppRoles.Dispatcher},{AppRoles.Initiator}")]
public sealed class SummaryController(SummaryReportService summary) : ControllerBase
{
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
}
