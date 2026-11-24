using System.Security.Claims;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages.Requests;

[Authorize]
public sealed class EditModel(CrownApiClient api, AppTime time) : PageModel
{
    /// <summary>Временно: СТ и оборудование в UI заблокированы.</summary>
    public const bool SpecAndEquipmentKindsLocked = true;

    public ServiceRequestDetail? Item { get; private set; }

    public string? Error { get; private set; }

    public List<EquipmentRequestCategory> EquipmentCategories { get; private set; } = [];

    public List<SpecialVehicleTypeAvailability> SpecTypeAvailability { get; private set; } = [];

    [BindProperty]
    public EditInputModel Input { get; set; } = new();

    public bool CanCreateTransport =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanCreateTransportClaim)?.Value == "1";

    private bool IsInitiatorOfItem =>
        Item?.InitiatorUserId is Guid iid
        && User.FindFirstValue(ClaimTypes.NameIdentifier) == iid.ToString();

    public bool CanEdit =>
        Item is not null
        && !Item.IsArchived
        && (User.IsInRole(AppRoles.Administrator)
            || User.IsInRole(AppRoles.Dispatcher)
            || IsInitiatorOfItem)
        && (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher)
            ? Item.Status is ServiceRequestStatus.Submitted
                or ServiceRequestStatus.Returned
                or ServiceRequestStatus.InProgress
            : Item.Status is ServiceRequestStatus.Submitted or ServiceRequestStatus.Returned);

    public async Task<IActionResult> OnGetAsync(long id)
    {
        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        if (Item is null)
        {
            return Error is null ? NotFound() : Page();
        }

        if (!CanEdit)
        {
            return Redirect($"/requests/{id}");
        }

        SeedInput();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(long id)
    {
        var redirect = await LoadAsync(id);
        if (redirect is not null)
        {
            return redirect;
        }

        if (Item is null)
        {
            return Error is null ? NotFound() : Page();
        }

        if (!CanEdit)
        {
            return Redirect($"/requests/{id}");
        }

        // Заблокированные типы: нельзя переключить на СТ/оборудование; текущий Kind сохраняем.
        if (SpecAndEquipmentKindsLocked
            && Input.Kind is ServiceRequestKind.SpecTech or ServiceRequestKind.Equipment
            && Input.Kind != Item.Kind)
        {
            Input.Kind = Item.Kind;
        }

        if (string.IsNullOrWhiteSpace(Input.EditReason))
        {
            Error = "Укажите причину редактирования.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Input.Phone))
        {
            Error = "Телефон обязателен.";
            return Page();
        }

        if (!time.TryParseInput(Input.DueAt, out var dueAt))
        {
            Error = "Укажите срок исполнения.";
            return Page();
        }

        DateTimeOffset? specPlanned = null;
        if (Input.Kind == ServiceRequestKind.SpecTech)
        {
            if (!CanCreateTransport && Item.Kind != ServiceRequestKind.SpecTech)
            {
                Error = "Нет права на заявки спецтехники.";
                return Page();
            }

            if (!time.TryParseInput(Input.SpecPlannedAt, out var planned))
            {
                Error = "Укажите плановое время.";
                return Page();
            }

            specPlanned = planned;
        }

        var result = await api.PutAsync<ServiceRequestDetail>(
            $"api/requests/{id}",
            new EditServiceRequestRequest
            {
                Phone = Input.Phone!.Trim(),
                DueAt = dueAt,
                Body = Input.Body?.Trim() ?? string.Empty,
                IsUrgent = Input.IsUrgent,
                Kind = Input.Kind,
                RequestedEquipmentCategory = Input.Kind == ServiceRequestKind.Equipment
                    ? Input.RequestedEquipmentCategory
                    : null,
                RequestedSpecialVehicleType = Input.Kind == ServiceRequestKind.SpecTech
                    ? Input.RequestedSpecialVehicleType
                    : null,
                SpecOperation = Input.Kind == ServiceRequestKind.SpecTech
                    ? Input.SpecOperation
                    : null,
                SpecPlannedAt = specPlanned,
                EditReason = Input.EditReason.Trim(),
                RowVersion = Item.RowVersion
            },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось сохранить.";
            return Page();
        }

        TempData["Ok"] = "Заявка обновлена.";
        return Redirect($"/requests/{id}");
    }

    private async Task<IActionResult?> LoadAsync(long id)
    {
        var result = await api.GetAsync<ServiceRequestDetail>($"api/requests/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (result.Status == 404)
        {
            return NotFound();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error;
            return null;
        }

        Item = result.Data;

        var cats = await api.GetAsync<List<EquipmentRequestCategory>>(
            "api/equipment/request-categories",
            HttpContext.RequestAborted);
        if (cats.Ok && cats.Data is not null)
        {
            EquipmentCategories = cats.Data;
        }

        var avail = await api.GetAsync<SpecialVehicleAvailabilityPayload>(
            "api/special-vehicles/availability",
            HttpContext.RequestAborted);
        if (avail.Ok && avail.Data is not null)
        {
            SpecTypeAvailability = avail.Data.Types;
        }

        return null;
    }

    private void SeedInput()
    {
        if (Item is null)
        {
            return;
        }

        Input = new EditInputModel
        {
            Phone = Item.Phone,
            DueAt = time.ToInput(Item.DueAt),
            Body = Item.Body,
            IsUrgent = Item.IsUrgent,
            Kind = Item.Kind,
            RequestedEquipmentCategory = Item.RequestedEquipmentCategory,
            RequestedSpecialVehicleType = Item.RequestedSpecialVehicleType,
            SpecOperation = Item.SpecOperation,
            SpecPlannedAt = Item.SpecPlannedAt is DateTimeOffset spa ? time.ToInput(spa) : null,
            EditReason = string.Empty
        };
    }

    public sealed class EditInputModel
    {
        public string? Phone { get; set; }
        public string? DueAt { get; set; }
        public string? Body { get; set; }
        public bool IsUrgent { get; set; }
        public ServiceRequestKind Kind { get; set; } = ServiceRequestKind.Other;
        public string? RequestedEquipmentCategory { get; set; }
        public string? RequestedSpecialVehicleType { get; set; }
        public string? SpecOperation { get; set; }
        public string? SpecPlannedAt { get; set; }
        public string? EditReason { get; set; }
    }
}
