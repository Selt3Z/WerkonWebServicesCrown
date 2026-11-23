using Crown.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class DocumentationSettingsModel : PageModel
{
    public IActionResult OnGet() => RedirectPermanent("/settings/documentation-access");
}
