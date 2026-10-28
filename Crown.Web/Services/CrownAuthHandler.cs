using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Crown.Web.Services;

/// <summary>
/// Схема аутентификации Web: пользователь определяется через GET api/auth/me
/// с прокинутой cookie Crown.Auth. Собственной cookie у Web нет — источник истины API.
/// </summary>
public sealed class CrownAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "CrownApi";
    public const string DisplayNameClaim = "display_name";

    private readonly CrownApiClient _api;

    public CrownAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CrownApiClient api)
        : base(options, logger, encoder)
    {
        _api = api;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = await _api.GetCurrentUserAsync(Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.NoResult();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(DisplayNameClaim, user.DisplayName)
        };
        claims.AddRange(user.Roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        // returnUrl имеет смысл только для GET: после POST вернуться некуда.
        var target = "/login";
        if (HttpMethods.IsGet(Request.Method))
        {
            var back = Request.Path + Request.QueryString;
            if (back.Length > 1)
            {
                target += "?returnUrl=" + Uri.EscapeDataString(back);
            }
        }

        Response.Redirect(target);
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/access-denied");
        return Task.CompletedTask;
    }
}
