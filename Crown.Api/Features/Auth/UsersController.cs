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
    // Длины колонок AspNetUsers.BrigadeNumber / BrigadeWorkshop (см. AppDbContext).
    private const int BrigadeNumberMaxLength = 50;
    private const int BrigadeWorkshopMaxLength = 100;

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

        // Настройки пользователя — сырое имя без « №…» (номер правится отдельным полем).
        return Ok(await MapListItemAsync(user, forDisplay: false));
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

        var role = request.Role.Trim();
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName.Trim(),
            DisplayName = request.DisplayName.Trim(),
            // Заголовок раздела — только у роли Department (у остальных нет своего раздела).
            DocumentationTitle = IsDepartmentRole(role)
                ? NormalizeDocumentationTitle(request.DocumentationTitle)
                : null,
            // Номер/цех — только у роли Brigade.
            BrigadeNumber = IsBrigadeRole(role) ? NormalizeBrigadeField(request.BrigadeNumber) : null,
            BrigadeWorkshop = IsBrigadeRole(role) ? NormalizeBrigadeField(request.BrigadeWorkshop) : null,
            Email = $"{request.UserName.Trim()}@local",
            EmailConfirmed = true,
            IsActive = request.IsActive
        };

        var createResult = await _userManager.CreateAsync(user, request.Password!);

        if (!createResult.Succeeded)
        {
            return BadRequest(ApiErrors.FromIdentityErrors(createResult.Errors));
        }

        await _userManager.AddToRoleAsync(user, role);

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

        var newRole = request.Role.Trim();
        var roleChanged = !string.Equals(currentRole, newRole, StringComparison.Ordinal);

        // Смена роли разрешена только внутри тройки Department/Customer/Brigade (и с обеих сторон).
        // Проверяем ДО любых изменений, чтобы не сохранить профиль при отказе по роли.
        if (roleChanged
            && (!AppRoles.IsSwitchableRole(currentRole) || !AppRoles.IsSwitchableRole(newRole)))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.RoleImmutable));
        }

        var currentUser = await _userManager.GetUserAsync(User);

        if (!request.IsActive && currentUser is not null && currentUser.Id == user.Id)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.CannotDeactivateOwnAccount));
        }

        user.DisplayName = request.DisplayName.Trim();
        // Поля зависят от НОВОЙ роли: при уходе с Department/Brigade значения очищаются,
        // при входе в роль берутся из запроса.
        user.DocumentationTitle = IsDepartmentRole(newRole)
            ? NormalizeDocumentationTitle(request.DocumentationTitle)
            : null;
        user.BrigadeNumber = IsBrigadeRole(newRole) ? NormalizeBrigadeField(request.BrigadeNumber) : null;
        user.BrigadeWorkshop = IsBrigadeRole(newRole) ? NormalizeBrigadeField(request.BrigadeWorkshop) : null;
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

        // Переключение роли внутри Department/Customer/Brigade (проверено выше).
        // Protected admin сюда не попадает: его роль сверена на ProtectedAdminRoleImmutable.
        if (roleChanged)
        {
            if (!string.IsNullOrEmpty(currentRole))
            {
                var removeResult = await _userManager.RemoveFromRoleAsync(user, currentRole);
                if (!removeResult.Succeeded)
                {
                    return BadRequest(ApiErrors.FromIdentityErrors(removeResult.Errors));
                }
            }

            var addResult = await _userManager.AddToRoleAsync(user, newRole);
            if (!addResult.Succeeded)
            {
                return BadRequest(ApiErrors.FromIdentityErrors(addResult.Errors));
            }
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

        // Снимок ФИО в заявках до SetNull по FK (имя остаётся текстом, у бригады — с номером).
        var snapshotName = UserDisplay.Format(user);
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

        // Dispatcher / Department — только свой профиль
        if (!User.IsInRole(AppRoles.Dispatcher) && !User.IsInRole(AppRoles.Department))
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
        // Self: только Department может менять заголовок раздела документации.
        if (await _userManager.IsInRoleAsync(user, AppRoles.Department))
        {
            user.DocumentationTitle = NormalizeDocumentationTitle(request.DocumentationTitle);
        }

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

        // Для роли Brigade номер и цех обязательны (и ограничены длиной колонок).
        if (IsBrigadeRole(request.Role.Trim()))
        {
            var number = NormalizeBrigadeField(request.BrigadeNumber);
            var workshop = NormalizeBrigadeField(request.BrigadeWorkshop);

            if (number is null)
            {
                return ApiErrorCodes.BrigadeNumberRequired;
            }

            if (workshop is null)
            {
                return ApiErrorCodes.BrigadeWorkshopRequired;
            }

            if (number.Length > BrigadeNumberMaxLength)
            {
                return ApiErrorCodes.BrigadeNumberTooLong;
            }

            if (workshop.Length > BrigadeWorkshopMaxLength)
            {
                return ApiErrorCodes.BrigadeWorkshopTooLong;
            }
        }

        return null;
    }

    /// <param name="forDisplay">
    /// true — в списках: у бригады имя + « №…»; false — форма редактирования (сырое DisplayName).
    /// </param>
    private async Task<UserListItemResponse> MapListItemAsync(ApplicationUser user, bool forDisplay = true)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var role = roles.OrderBy(x => x).FirstOrDefault() ?? string.Empty;
        var isBrigade = IsBrigadeRole(role);

        return new UserListItemResponse
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            DisplayName = forDisplay
                ? UserDisplay.FormatUserDisplay(user, isBrigade)
                : (user.DisplayName ?? string.Empty),
            DocumentationTitle = user.DocumentationTitle,
            BrigadeNumber = user.BrigadeNumber,
            BrigadeWorkshop = user.BrigadeWorkshop,
            Role = role,
            IsActive = user.IsActive,
            LastActivityAtUtc = user.LastActivityAtUtc
        };
    }

    private static string? NormalizeDocumentationTitle(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static bool IsDepartmentRole(string? role) =>
        string.Equals(role, AppRoles.Department, StringComparison.Ordinal);

    private static bool IsBrigadeRole(string? role) =>
        string.Equals(role, AppRoles.Brigade, StringComparison.Ordinal);

    private static string? NormalizeBrigadeField(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    // Seed:AdminUserName по умолчанию "admin" — логин/роль не меняем из UI.
    private static bool IsProtectedAdminUser(ApplicationUser user) =>
        string.Equals(user.UserName, "admin", StringComparison.OrdinalIgnoreCase);
}
