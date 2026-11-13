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

    public DocumentationGlobalSearch? Search { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var sectionsTask = api.GetAsync<List<DocumentationSection>>(
            "api/documentation/sections",
            HttpContext.RequestAborted);

        var q = (Q ?? string.Empty).Trim();
        Task<ApiResult<DocumentationGlobalSearch>>? searchTask = null;
        if (q.Length >= 2)
        {
            searchTask = api.GetAsync<DocumentationGlobalSearch>(
                $"api/documentation/search?q={Uri.EscapeDataString(q)}",
                HttpContext.RequestAborted);
        }

        var sections = await sectionsTask;
        if (sections.IsUnauthorized)
        {
            return Challenge();
        }

        if (!sections.Ok || sections.Data is null)
        {
            Error = sections.Error;
            return Page();
        }

        Sections = sections.Data;

        if (searchTask is not null)
        {
            var search = await searchTask;
            if (search.IsUnauthorized)
            {
                return Challenge();
            }

            if (!search.Ok || search.Data is null)
            {
                Error = search.Error;
            }
            else
            {
                Search = search.Data;
            }
        }

        return Page();
    }
}
