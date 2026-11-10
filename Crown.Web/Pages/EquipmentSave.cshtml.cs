using System.Security.Claims;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class EquipmentSaveModel(CrownApiClient api) : PageModel
{
    private bool CanAccess =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanManageEquipmentClaim)?.Value == "1";

    /// <summary>Менять «Принадлежит» могут только админ и диспетчер.</summary>
    public bool CanEditOwner =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    [BindProperty]
    public SaveEquipmentAssetRequest Input { get; set; } = new();

    public List<EquipmentLocationOption> LocationOptions { get; private set; } = [];

    public Guid? Id { get; private set; }

    public bool IsEdit => Id is not null;

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        Id = id;
        await LoadLocationsAsync();
        if (id is null)
        {
            // Подразделение при создании — владелец по умолчанию = текущий пользователь.
            if (User.IsInRole(AppRoles.Department)
                && Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var me)
                && LocationOptions.Any(x => x.Id == me))
            {
                Input.OwnerUserId = me;
            }

            return Page();
        }

        var part = await api.GetAsync<EquipmentAssetDetail>($"api/equipment/{id}", HttpContext.RequestAborted);
        if (part.IsUnauthorized)
        {
            return Challenge();
        }

        if (!part.Ok || part.Data is null)
        {
            Error = part.Error ?? "Не найдено.";
            return Page();
        }

        Input = new SaveEquipmentAssetRequest
        {
            Name = part.Data.Name,
            InventoryNumber = part.Data.InventoryNumber,
            Category = part.Data.Category,
            Description = part.Data.Description,
            CurrentLocationUserId = part.Data.CurrentLocationUserId,
            CurrentLocationLabel = part.Data.CurrentLocationLabel,
            OwnerUserId = part.Data.OwnerUserId,
            OwnerLabel = part.Data.OwnerLabel
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        Id = id;
        await LoadLocationsAsync();

        if (Input.CurrentLocationUserId is null || Input.CurrentLocationUserId == Guid.Empty)
        {
            Error = "Укажите текущее местоположение (подразделение).";
            return Page();
        }

        var result = id is Guid existing
            ? await api.PutAsync<EquipmentAsset>($"api/equipment/{existing}", Input, HttpContext.RequestAborted)
            : await api.PostAsync<EquipmentAsset>("api/equipment", Input, HttpContext.RequestAborted);

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
        return Redirect($"/equipment/{result.Data.Id}");
    }

    public async Task<IActionResult> OnPostImportPreviewAsync(IFormFile? file)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        if (file is null || file.Length == 0)
        {
            return new JsonResult(new { error = "file_required", message = "Выберите файл." }) { StatusCode = 400 };
        }

        var result = await api.UploadFileAsync<SpreadsheetImportPreview>(
            "api/equipment/import/preview",
            file,
            "file",
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return new JsonResult(new { error = "preview_failed", message = result.Error ?? "Ошибка." })
            {
                StatusCode = 400
            };
        }

        return new JsonResult(result.Data);
    }

    public async Task<IActionResult> OnPostImportCommitAsync([FromBody] SpreadsheetImportCommitRequest body)
    {
        if (!CanAccess)
        {
            return Forbid();
        }

        var result = await api.PostAsync<SpreadsheetImportCommitResult>(
            "api/equipment/import/commit",
            body,
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return new JsonResult(new { error = "commit_failed", message = result.Error ?? "Ошибка." })
            {
                StatusCode = 400
            };
        }

        return new JsonResult(result.Data);
    }

    private async Task LoadLocationsAsync()
    {
        var part = await api.GetAsync<List<EquipmentLocationOption>>(
            "api/equipment/location-options",
            HttpContext.RequestAborted);
        LocationOptions = part.Ok && part.Data is not null ? part.Data : [];
    }
}
