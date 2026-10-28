using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class WarehouseSaveModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public SaveWarehousePartRequest Input { get; set; } = new();

    public List<string> Categories { get; private set; } = [];

    public string? Error { get; private set; }

    public bool IsEdit => Id is Guid;

    public async Task<IActionResult> OnGetAsync()
    {
        var categories = await api.GetAsync<List<string>>("api/warehouse-parts/categories", HttpContext.RequestAborted);
        if (categories.Status == 404)
        {
            Error = "Складской учёт отключён. Включите его в разделе «Настройки».";
            return Page();
        }

        Categories = categories.Data ?? [];

        if (Id is Guid id)
        {
            var part = await api.GetAsync<WarehousePart>($"api/warehouse-parts/{id}", HttpContext.RequestAborted);
            if (part.IsUnauthorized)
            {
                return Challenge();
            }

            if (!part.Ok || part.Data is null)
            {
                Error = part.Error ?? "Позиция не найдена.";
                return Page();
            }

            Input = new SaveWarehousePartRequest
            {
                Name = part.Data.Name,
                Code = part.Data.Code ?? string.Empty,
                Barcode = part.Data.Barcode,
                Category = part.Data.Category,
                DefaultUnitPrice = part.Data.DefaultUnitPrice,
                DefaultUnit = part.Data.DefaultUnit,
                QuantityOnHand = part.Data.QuantityOnHand,
                MinQuantityOnHand = part.Data.MinQuantityOnHand,
                Description = part.Data.Description
            };
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var categories = await api.GetAsync<List<string>>("api/warehouse-parts/categories", HttpContext.RequestAborted);
        Categories = categories.Data ?? [];

        if (string.IsNullOrWhiteSpace(Input.Name) || string.IsNullOrWhiteSpace(Input.Code))
        {
            Error = "Название и код обязательны.";
            return Page();
        }

        var result = Id is Guid id
            ? await api.PutAsync<WarehousePart>($"api/warehouse-parts/{id}", Input, HttpContext.RequestAborted)
            : await api.PostAsync<WarehousePart>("api/warehouse-parts", Input, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Status == 404)
        {
            Error = "Складской учёт отключён. Включите его в разделе «Настройки».";
            return Page();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Ok"] = IsEdit ? "Позиция склада обновлена." : "Позиция склада добавлена.";
        return RedirectToPage("/Warehouse");
    }
}
