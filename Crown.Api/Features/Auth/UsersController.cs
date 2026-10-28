using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Auth;
using Crown.Contracts.Common;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

namespace Crown.Features.Auth;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole<Guid>> _roleManager;
    private readonly AppDbContext _db;

    public UsersController(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        AppDbContext db)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _db = db;
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<PagedResponse<UserListItemResponse>>> GetAll(
        [FromQuery] int? skip,
        [FromQuery] int? take,
        CancellationToken cancellationToken)
    {
        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);

        var users = await _userManager.Users
            .OrderBy(x => x.UserName)
            .ThenBy(x => x.Id)
            .Skip(normalizedSkip)
            .Take(normalizedTake + 1)
            .ToListAsync(cancellationToken);

        var hasMore = users.Count > normalizedTake;

        if (hasMore)
        {
            users.RemoveAt(users.Count - 1);
        }

        var items = new List<UserListItemResponse>();

        foreach (var user in users)
        {
            items.Add(await MapListItemAsync(user));
        }

        return Ok(new PagedResponse<UserListItemResponse>
        {
            Items = items,
            HasMore = hasMore
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserListItemResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.Users
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (!await CanAccessUserAsync(user.Id, cancellationToken))
        {
            return Forbid();
        }

        return Ok(await MapListItemAsync(user));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<ActionResult<UserListItemResponse>> Create(
        SaveUserRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = await ValidateRequestAsync(
            request,
            isEdit: false,
            currentRole: null,
            cancellationToken);

        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName.Trim(),
            DisplayName = request.DisplayName.Trim(),
            Email = $"{request.UserName.Trim()}@local",
            EmailConfirmed = true,
            IsActive = request.IsActive
        };

        var createResult = await _userManager.CreateAsync(user, request.Password!);

        if (!createResult.Succeeded)
        {
            return BadRequest(ApiErrors.FromIdentityErrors(createResult.Errors));
        }

        await _userManager.AddToRoleAsync(user, request.Role.Trim());

        return CreatedAtAction(nameof(GetById), new { id = user.Id }, await MapListItemAsync(user));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserListItemResponse>> Update(
        Guid id,
        SaveUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userManager.Users
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        if (!await CanAccessUserAsync(user.Id, cancellationToken))
        {
            return Forbid();
        }

        var isSelfProfileEdit = await IsSelfProfileEditAsync(user.Id, cancellationToken);

        if (isSelfProfileEdit)
        {
            return await UpdateSelfProfileAsync(user, request, cancellationToken);
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        var currentRole = currentRoles.OrderBy(x => x).FirstOrDefault() ?? string.Empty;
        var isProtectedAdmin = IsProtectedAdminUser(user);

        var validationError = await ValidateRequestAsync(
            request,
            isEdit: true,
            currentRole,
            cancellationToken);

        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        if (isProtectedAdmin)
        {
            if (!string.Equals(user.UserName, request.UserName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(ApiErrors.FromCode(ApiErrorCodes.ProtectedAdminLoginImmutable));
            }

            if (!string.Equals(currentRole, request.Role.Trim(), StringComparison.Ordinal))
            {
                return BadRequest(ApiErrors.FromCode(ApiErrorCodes.ProtectedAdminRoleImmutable));
            }
        }

        var currentUser = await _userManager.GetUserAsync(User);

        if (!request.IsActive && currentUser is not null && currentUser.Id == user.Id)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.CannotDeactivateOwnAccount));
        }

        user.DisplayName = request.DisplayName.Trim();
        // Seed admin нельзя деактивировать из UI/API — иначе потеряем вход в систему.
        user.IsActive = isProtectedAdmin || request.IsActive;

        if (!isProtectedAdmin
            && !string.Equals(user.UserName, request.UserName.Trim(), StringComparison.Ordinal))
        {
            var setUserNameResult = await _userManager.SetUserNameAsync(user, request.UserName.Trim());

            if (!setUserNameResult.Succeeded)
            {
                return BadRequest(ApiErrors.FromIdentityErrors(setUserNameResult.Errors));
            }

            await _userManager.SetEmailAsync(user, $"{request.UserName.Trim()}@local");
        }

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            // Свой пароль — только через CurrentPassword; чужой (admin) — reset без текущего.
            var isSelfPasswordChange = currentUser is not null && currentUser.Id == user.Id;
            var passwordError = await ApplyPasswordChangeAsync(user, request, isSelfPasswordChange);
            if (passwordError is not null)
            {
                return passwordError;
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            return BadRequest(ApiErrors.FromIdentityErrors(updateResult.Errors));
        }

        var targetRole = isProtectedAdmin ? currentRole : request.Role.Trim();

        if (!currentRoles.Contains(targetRole))
        {
            if (currentRoles.Count > 0)
            {
                await _userManager.RemoveFromRolesAsync(user, currentRoles);
            }

            await _userManager.AddToRoleAsync(user, targetRole);
        }

        return Ok(await MapListItemAsync(user));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageUsers)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var user = await _userManager.Users
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (user is null)
        {
            return NotFound();
        }

        var currentUser = await _userManager.GetUserAsync(User);

        if (currentUser is not null && currentUser.Id == user.Id)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.CannotDeleteOwnAccount));
        }

        if (IsProtectedAdminUser(user))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.ProtectedAdminCannotDelete));
        }

        // Снимок ФИО в заявках до SetNull по FK (имя остаётся текстом).
        var snapshotName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? (user.UserName ?? string.Empty)
            : user.DisplayName;
        await _db.ServiceRequests
            .IgnoreQueryFilters()
            .Where(x => x.InitiatorUserId == user.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.InitiatorName, snapshotName),
                cancellationToken);
        await _db.ServiceRequests
            .IgnoreQueryFilters()
            .Where(x => x.ExecutorUserId == user.Id)
            .ExecuteUpdateAsync(
                s => s.SetProperty(x => x.ExecutorName, snapshotName),
                cancellationToken);

        var deleteResult = await _userManager.DeleteAsync(user);

        if (!deleteResult.Succeeded)
        {
            return BadRequest(ApiErrors.FromIdentityErrors(deleteResult.Errors));
        }

        return NoContent();
    }

    private async Task<bool> CanAccessUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (User.IsInRole(AppRoles.Administrator))
        {
            return true;
        }

        // Dispatcher / Initiator — только свой профиль
        if (!User.IsInRole(AppRoles.Dispatcher) && !User.IsInRole(AppRoles.Initiator))
        {
            return false;
        }

        var currentUser = await _userManager.GetUserAsync(User);
        return currentUser is not null && currentUser.Id == userId;
    }

    private async Task<bool> IsSelfProfileEditAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (User.IsInRole(AppRoles.Administrator))
        {
            return false;
        }

        var currentUser = await _userManager.GetUserAsync(User);
        return currentUser is not null && currentUser.Id == userId;
    }

    private async Task<ActionResult<UserListItemResponse>> UpdateSelfProfileAsync(
        ApplicationUser user,
        SaveUserRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.DisplayNameRequired));
        }

        user.DisplayName = request.DisplayName.Trim();

        if (!string.IsNullOrWhiteSpace(request.Password))
        {
            var passwordError = await ApplyPasswordChangeAsync(user, request, isSelfPasswordChange: true);
            if (passwordError is not null)
            {
                return passwordError;
            }
        }

        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            return BadRequest(ApiErrors.FromIdentityErrors(updateResult.Errors));
        }

        return Ok(await MapListItemAsync(user));
    }

    private async Task<ActionResult?> ApplyPasswordChangeAsync(
        ApplicationUser user,
        SaveUserRequest request,
        bool isSelfPasswordChange)
    {
        if (isSelfPasswordChange)
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            {
                return BadRequest(ApiErrors.FromCode(ApiErrorCodes.CurrentPasswordRequired));
            }

            var changeResult = await _userManager.ChangePasswordAsync(
                user,
                request.CurrentPassword,
                request.Password!);

            if (!changeResult.Succeeded)
            {
                // Неверный текущий пароль — отдельный код; остальные ошибки Identity как есть.
                if (changeResult.Errors.Any(e =>
                        string.Equals(e.Code, "PasswordMismatch", StringComparison.OrdinalIgnoreCase)))
                {
                    return BadRequest(ApiErrors.FromCode(ApiErrorCodes.CurrentPasswordInvalid));
                }

                return BadRequest(ApiErrors.FromIdentityErrors(changeResult.Errors));
            }

            return null;
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var passwordResult = await _userManager.ResetPasswordAsync(user, token, request.Password!);

        if (!passwordResult.Succeeded)
        {
            return BadRequest(ApiErrors.FromIdentityErrors(passwordResult.Errors));
        }

        return null;
    }

    private async Task<string?> ValidateRequestAsync(
        SaveUserRequest request,
        bool isEdit,
        string? currentRole,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName))
        {
            return ApiErrorCodes.UserNameRequired;
        }

        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return ApiErrorCodes.DisplayNameRequired;
        }

        if (string.IsNullOrWhiteSpace(request.Role) || !AppRoles.All.Contains(request.Role))
        {
            return ApiErrorCodes.RoleRequired;
        }

        if (!isEdit && string.IsNullOrWhiteSpace(request.Password))
        {
            return ApiErrorCodes.PasswordRequired;
        }

        if (!await _roleManager.RoleExistsAsync(request.Role))
        {
            return ApiErrorCodes.RoleNotFound;
        }

        return null;
    }

    private async Task<UserListItemResponse> MapListItemAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);

        return new UserListItemResponse
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Role = roles.OrderBy(x => x).FirstOrDefault() ?? string.Empty,
            IsActive = user.IsActive,
            LastActivityAtUtc = user.LastActivityAtUtc
        };
    }

    // Seed:AdminUserName по умолчанию "admin" — логин/роль не меняем из UI.
    private static bool IsProtectedAdminUser(ApplicationUser user) =>
        string.Equals(user.UserName, "admin", StringComparison.OrdinalIgnoreCase);
}
