using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class CatalogPartSaveModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public SaveCatalogPartRequest Input { get; set; } = new() { IsActive = true };

    public List<string> Categories { get; private set; } = [];

    public string? Error { get; private set; }

    public bool IsEdit => Id is Guid;

    public async Task<IActionResult> OnGetAsync()
    {
        var categories = await api.GetAsync<List<string>>("api/catalog-parts/categories", HttpContext.RequestAborted);
        Categories = categories.Data ?? [];

        if (Id is Guid id)
        {
            var part = await api.GetAsync<CatalogPart>($"api/catalog-parts/{id}", HttpContext.RequestAborted);
            if (part.IsUnauthorized)
            {
                return Challenge();
            }

            if (!part.Ok || part.Data is null)
            {
                Error = part.Error ?? "Запчасть не найдена.";
                return Page();
            }

            Input = new SaveCatalogPartRequest
            {
                Name = part.Data.Name,
                Code = part.Data.Code,
                Category = part.Data.Category,
                DefaultUnitPrice = part.Data.DefaultUnitPrice,
                DefaultUnit = part.Data.DefaultUnit,
                Description = part.Data.Description,
                IsActive = part.Data.IsActive
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var categories = await api.GetAsync<List<string>>("api/catalog-parts/categories", HttpContext.RequestAborted);
        Categories = categories.Data ?? [];

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            Error = "Название обязательно.";
            return Page();
        }

        var result = Id is Guid id
            ? await api.PutAsync<CatalogPart>($"api/catalog-parts/{id}", Input, HttpContext.RequestAborted)
            : await api.PostAsync<CatalogPart>("api/catalog-parts", Input, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Ok"] = IsEdit ? "Запчасть обновлена." : "Запчасть добавлена в каталог.";
        return RedirectToPage("/CatalogParts");
    }
}
