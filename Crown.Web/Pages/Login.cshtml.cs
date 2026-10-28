using Crown.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

public sealed class LoginModel(CrownApiClient api) : PageModel
{
    [BindProperty]
    public string UserName { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Уже вошли — на главную/returnUrl.
        if (await api.GetCurrentUserAsync(HttpContext.RequestAborted) is not null)
        {
            return LocalRedirect(SafeReturnUrl());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrWhiteSpace(UserName) || string.IsNullOrWhiteSpace(Password))
        {
            Error = "Введите логин и пароль.";
            return Page();
        }

        var outcome = await api.LoginAsync(UserName.Trim(), Password, HttpContext.RequestAborted);
        if (!outcome.Success)
        {
            Error = outcome.Error;
            Password = string.Empty;
            return Page();
        }

        // Set-Cookie от API уже скопирован в ответ браузеру внутри LoginAsync.
        return LocalRedirect(SafeReturnUrl());
    }

    /// <summary>Защита от open redirect: только локальные пути.</summary>
    private string SafeReturnUrl() =>
        !string.IsNullOrWhiteSpace(ReturnUrl) && Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/";
}
