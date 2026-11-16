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
        // Собираем (пользователь, роль) по трём ролям матрицы; у пользователя одна роль,
        // но на случай нескольких берём первую по порядку SwitchableRoles.
        var rows = new Dictionary<Guid, (ApplicationUser User, string Role)>();
        foreach (var role in AppRoles.SwitchableRoles)
        {
            foreach (var user in await _users.GetUsersInRoleAsync(role))
            {
                rows.TryAdd(user.Id, (user, role));
            }
        }

        var items = rows.Values
            .OrderByDescending(x => x.User.IsActive)
            .ThenBy(x => x.User.DisplayName)
            .ThenBy(x => x.User.UserName)
            .Select(x => Map(x.User, x.Role))
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

            // Только Department / Customer / Brigade — админов/диспетчеров не трогаем.
            if (!await IsMatrixRoleAsync(user))
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

    private async Task<bool> IsMatrixRoleAsync(ApplicationUser user)
    {
        foreach (var role in AppRoles.SwitchableRoles)
        {
            if (await _users.IsInRoleAsync(user, role))
            {
                return true;
            }
        }

        return false;
    }

    private static DepartmentPermissionUserResponse Map(ApplicationUser x, string role) => new()
    {
        UserId = x.Id,
        // Бригада — «Имя №номер», остальные — просто имя (общий helper).
        DisplayName = UserDisplay.FormatUserDisplay(x, isBrigade: role == AppRoles.Brigade),
        UserName = x.UserName ?? string.Empty,
        Role = role,
        BrigadeNumber = role == AppRoles.Brigade ? x.BrigadeNumber : null,
        BrigadeWorkshop = role == AppRoles.Brigade ? x.BrigadeWorkshop : null,
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
