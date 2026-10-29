using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class CatalogPartSaveModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? OwnerUserId { get; set; }

    [BindProperty]
    public SaveCatalogPartRequest Input { get; set; } = new() { IsActive = true, DefaultUnit = "шт" };

    public List<string> Categories { get; private set; } = [];

    public List<RequestAssignee> Owners { get; private set; } = [];

    public string? Error { get; private set; }

    public bool IsEdit => Id is Guid;

    public bool IsStaff =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsStaff)
        {
            await LoadOwnersAsync();
            if (Id is null && OwnerUserId is null)
            {
                Error = "Выберите подразделение на странице каталога.";
                return Page();
            }
        }

        if (Id is Guid id)
        {
            var part = await api.GetAsync<CatalogPart>($"api/catalog-parts/{id}", HttpContext.RequestAborted);
            if (part.IsUnauthorized)
            {
                return Challenge();
            }

            if (!part.Ok || part.Data is null)
            {
                Error = part.Error ?? "Услуга не найдена.";
                return Page();
            }

            OwnerUserId = part.Data.OwnerUserId;
            Input = new SaveCatalogPartRequest
            {
                OwnerUserId = part.Data.OwnerUserId,
                Name = part.Data.Name,
                Code = part.Data.Code,
                Category = part.Data.Category,
                DefaultUnitPrice = part.Data.DefaultUnitPrice,
                DefaultUnit = string.IsNullOrWhiteSpace(part.Data.DefaultUnit) ? "шт" : part.Data.DefaultUnit,
                Description = part.Data.Description,
                SlaHours = part.Data.SlaHours,
                IsActive = part.Data.IsActive
            };
        }
        else if (IsStaff)
        {
            Input.OwnerUserId = OwnerUserId;
        }

        await LoadCategoriesAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (IsStaff)
        {
            await LoadOwnersAsync();
            Input.OwnerUserId ??= OwnerUserId;
            if (Input.OwnerUserId is null || Input.OwnerUserId == Guid.Empty)
            {
                Error = "Укажите подразделение-владельца каталога.";
                return Page();
            }
        }

        await LoadCategoriesAsync();

        if (string.IsNullOrWhiteSpace(Input.Name))
        {
            Error = "Название обязательно.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.DefaultUnit))
        {
            Input.DefaultUnit = "шт";
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

        TempData["Ok"] = IsEdit ? "Услуга обновлена." : "Услуга добавлена в каталог.";
        var backOwner = Input.OwnerUserId ?? OwnerUserId;
        return IsStaff && backOwner is Guid oid
            ? Redirect($"/catalog-parts?ownerUserId={oid}")
            : RedirectToPage("/CatalogParts");
    }

    private async Task LoadOwnersAsync()
    {
        var result = await api.GetAsync<List<RequestAssignee>>("api/requests/assignees", HttpContext.RequestAborted);
        if (result.Ok && result.Data is not null)
        {
            Owners = result.Data;
        }
    }

    private async Task LoadCategoriesAsync()
    {
        var owner = IsStaff ? (Input.OwnerUserId ?? OwnerUserId) : null;
        var url = UiHelpers.BuildUrl(
            "api/catalog-parts/categories",
            ("ownerUserId", owner?.ToString()));
        var categories = await api.GetAsync<List<string>>(url, HttpContext.RequestAborted);
        Categories = categories.Data ?? [];
    }
}
