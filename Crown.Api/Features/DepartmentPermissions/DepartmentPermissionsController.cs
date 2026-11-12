using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.DepartmentPermissions;
using Crown.Infrastructure.Identity;

namespace Crown.Features.DepartmentPermissions;

[ApiController]
[Route("api/department-permissions")]
[Authorize(Policy = AuthorizationPolicies.ManageUsers)]
public sealed class DepartmentPermissionsController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;

    public DepartmentPermissionsController(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<List<DepartmentPermissionUserResponse>>> GetList(
        CancellationToken cancellationToken)
    {
        var inRole = await _users.GetUsersInRoleAsync(AppRoles.Department);
        var items = inRole
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Select(Map)
            .ToList();

        return Ok(items);
    }

    [HttpPut]
    public async Task<IActionResult> Update(
        [FromBody] UpdateDepartmentPermissionsRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Users.Count == 0)
        {
            return NoContent();
        }

        var ids = request.Users.Select(x => x.UserId).Distinct().ToList();
        var users = await _users.Users
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);

        var map = request.Users.ToDictionary(x => x.UserId);
        foreach (var user in users)
        {
            if (!map.TryGetValue(user.Id, out var row))
            {
                continue;
            }

            // Только роль Department — админов/диспетчеров не трогаем.
            if (!await _users.IsInRoleAsync(user, AppRoles.Department))
            {
                continue;
            }

            user.CanBeExecutor = row.CanBeExecutor;
            user.CanCreateTransportRequests = row.CanCreateTransportRequests;
            user.CanManageEquipment = row.CanManageEquipment;
            user.CanViewTransport = row.CanViewTransport;
            user.CanViewSpecialVehicles = row.CanViewSpecialVehicles;
            user.CanViewWarehouse = row.CanViewWarehouse;
            user.CanViewSummary = row.CanViewSummary;
            user.CanViewBrigadeRating = row.CanViewBrigadeRating;
            user.CanOperateBrigadeRating = row.CanOperateBrigadeRating;
            user.DocumentationEnabled = row.DocumentationEnabled;
            await _users.UpdateAsync(user);
        }

        return NoContent();
    }

    private static DepartmentPermissionUserResponse Map(ApplicationUser x) => new()
    {
        UserId = x.Id,
        DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? (x.UserName ?? string.Empty) : x.DisplayName,
        UserName = x.UserName ?? string.Empty,
        IsActive = x.IsActive,
        CanBeExecutor = x.CanBeExecutor,
        CanCreateTransportRequests = x.CanCreateTransportRequests,
        CanManageEquipment = x.CanManageEquipment,
        CanViewTransport = x.CanViewTransport,
        CanViewSpecialVehicles = x.CanViewSpecialVehicles,
        CanViewWarehouse = x.CanViewWarehouse,
        CanViewSummary = x.CanViewSummary,
        CanViewBrigadeRating = x.CanViewBrigadeRating,
        CanOperateBrigadeRating = x.CanOperateBrigadeRating,
        DocumentationEnabled = x.DocumentationEnabled
    };
}
