using Crown.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

public sealed class LogoutModel(CrownApiClient api) : PageModel
{
    public IActionResult OnGet() => RedirectToPage("/Index");

    public async Task<IActionResult> OnPostAsync()
    {
        // API присылает Set-Cookie с истёкшим сроком (пробрасывается клиентом);
        // ExpireAuthCookies — подстраховка, если API недоступен.
        await api.LogoutAsync(HttpContext.RequestAborted);
        api.ExpireAuthCookies();
        return Redirect("/login");
    }
}
