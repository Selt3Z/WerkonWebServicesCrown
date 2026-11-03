using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class DocumentationModel(CrownApiClient api) : PageModel
{
    public List<DocumentationSection> Sections { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var result = await api.GetAsync<List<DocumentationSection>>(
            "api/documentation/sections",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            return Page();
        }

        Sections = result.Data;
        return Page();
    }
}
