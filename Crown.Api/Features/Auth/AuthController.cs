using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Crown.Contracts.Auth;
using Crown.Contracts.Common;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;

namespace Crown.Features.Auth;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager)
    {
        _signInManager = signInManager;
        _userManager = userManager;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<CurrentUserResponse>> Login(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.UserName) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.LoginCredentialsRequired));
        }

        var user = await _userManager.FindByNameAsync(request.UserName.Trim());

        if (user is null)
        {
            return Unauthorized(new LoginFailureResponse
            {
                Code = LoginFailureCodes.InvalidCredentials
            });
        }

        if (!user.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new LoginFailureResponse
            {
                Code = LoginFailureCodes.AccountDisabled
            });
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return StatusCode(StatusCodes.Status423Locked, new LoginFailureResponse
            {
                Code = LoginFailureCodes.AccountLockedOut,
                LockoutEndUtc = await _userManager.GetLockoutEndDateAsync(user)
            });
        }

        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            request.Password,
            lockoutOnFailure: true);

        if (signInResult.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked, new LoginFailureResponse
            {
                Code = LoginFailureCodes.AccountLockedOut,
                LockoutEndUtc = await _userManager.GetLockoutEndDateAsync(user)
            });
        }

        if (!signInResult.Succeeded)
        {
            return Unauthorized(new LoginFailureResponse
            {
                Code = LoginFailureCodes.InvalidCredentials
            });
        }

        await _signInManager.SignInAsync(user, isPersistent: true);

        return Ok(await MapCurrentUserAsync(user));
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserResponse>> GetCurrentUser()
    {
        var user = await _userManager.GetUserAsync(User);

        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(await MapCurrentUserAsync(user));
    }

    private async Task<CurrentUserResponse> MapCurrentUserAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);

        return new CurrentUserResponse
        {
            Id = user.Id,
            UserName = user.UserName ?? string.Empty,
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? user.UserName ?? string.Empty
                : user.DisplayName,
            Roles = roles.OrderBy(x => x).ToList()
        };
    }
}
