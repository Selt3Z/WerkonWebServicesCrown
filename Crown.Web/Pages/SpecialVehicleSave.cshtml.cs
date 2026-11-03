using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class SpecialVehicleSaveModel(CrownApiClient api) : PageModel
{
    [BindProperty]
    public SaveSpecialVehicleUnitRequest Input { get; set; } = new() { IsActive = true };

    public Guid? Id { get; private set; }

    public bool IsEdit => Id is not null;

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        Id = id;
        if (id is null)
        {
            return Page();
        }

        var unit = await api.GetAsync<SpecialVehicleUnit>($"api/special-vehicles/{id}", HttpContext.RequestAborted);
        if (unit.IsUnauthorized)
        {
            return Challenge();
        }

        if (!unit.Ok || unit.Data is null)
        {
            Error = unit.Error ?? "Не найдено.";
            return Page();
        }

        Input = new SaveSpecialVehicleUnitRequest
        {
            Name = unit.Data.Name,
            PlateOrNote = unit.Data.PlateOrNote,
            IsActive = unit.Data.IsActive
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id)
    {
        Id = id;
        var result = id is Guid existing
            ? await api.PutAsync<SpecialVehicleUnit>($"api/special-vehicles/{existing}", Input, HttpContext.RequestAborted)
            : await api.PostAsync<SpecialVehicleUnit>("api/special-vehicles", Input, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Ok"] = id is null ? "Единица добавлена." : "Сохранено.";
        return Redirect("/special-vehicles");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await api.DeleteAsync($"api/special-vehicles/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            TempData["Warn"] = result.Error;
            return Redirect($"/special-vehicles/edit/{id}");
        }

        TempData["Ok"] = "Удалено.";
        return Redirect("/special-vehicles");
    }
}
