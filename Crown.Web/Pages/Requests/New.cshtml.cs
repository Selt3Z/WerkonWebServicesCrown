using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages.Requests;

public sealed class NewModel(CrownApiClient api, AppTime time) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty]
    public List<IFormFile> Files { get; set; } = [];

    public List<RequestAssignee> Assignees { get; private set; } = [];

    public List<EquipmentAsset> FreeEquipment { get; private set; } = [];

    public List<SpecialVehicleUnit> FreeSpecUnits { get; private set; } = [];

    public List<SpecialVehicleTypeAvailability> SpecTypeAvailability { get; private set; } = [];

    public bool CanPickInitiator => User.IsInRole(AppRoles.Administrator);

    public bool CanCreateTransport =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanCreateTransportClaim)?.Value == "1";

    public string CurrentUserDisplayName { get; private set; } = string.Empty;

    public string? Error { get; private set; }

    public sealed class InputModel
    {
        public string? Phone { get; set; }
        public string? DueAt { get; set; }
        public Guid? InitiatorUserId { get; set; }
        public Guid? ExecutorUserId { get; set; }
        /// <summary>manual | catalog</summary>
        public string BodyMode { get; set; } = "manual";
        public Guid? CatalogPartId { get; set; }
        public string? Body { get; set; }
        public ServiceRequestKind Kind { get; set; } = ServiceRequestKind.Other;
        public bool IsUrgent { get; set; }
        public Guid? EquipmentAssetId { get; set; }
        public Guid? SpecialVehicleUnitId { get; set; }
        public string? SpecOperation { get; set; }
        public string? SpecPlannedAt { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        CurrentUserDisplayName = ResolveDisplayName();
        Input.DueAt = time.ToInput(time.Now.AddDays(1));
        return await LoadLookupsAsync() ?? Page();
    }

    /// <summary>Активные услуги/узлы каталога исполнителя — для второго выпадающего списка.</summary>
    public async Task<IActionResult> OnGetServicesJsonAsync(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
        {
            return new JsonResult(Array.Empty<object>());
        }

        var url = UiHelpers.BuildUrl(
            "api/catalog-parts",
            ("ownerUserId", ownerUserId.ToString()),
            ("activeFilter", "active"),
            ("skip", "0"),
            ("take", "500"));

        var result = await api.GetAsync<PagedResponse<CatalogPart>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return new JsonResult(Array.Empty<object>()) { StatusCode = 400 };
        }

        var payload = result.Data.Items.Select(x => new
        {
            id = x.Id,
            name = x.Name,
            code = x.Code,
            description = x.Description,
            slaHours = x.SlaHours
        });

        return new JsonResult(payload);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        CurrentUserDisplayName = ResolveDisplayName();
        var redirect = await LoadLookupsAsync();
        if (redirect is not null)
        {
            return redirect;
        }

        if (string.IsNullOrWhiteSpace(Input.Phone))
        {
            Error = "Телефон обязателен.";
            return Page();
        }

        if (!time.TryParseInput(Input.DueAt, out var dueAt))
        {
            Error = "Укажите срок исполнения (дата и время).";
            return Page();
        }

        if (CanPickInitiator && (Input.InitiatorUserId is null || Input.InitiatorUserId == Guid.Empty))
        {
            Error = "Выберите инициатора.";
            return Page();
        }

        if (Input.ExecutorUserId is null || Input.ExecutorUserId == Guid.Empty)
        {
            Error = "Выберите исполнителя.";
            return Page();
        }

        if (Input.Kind == ServiceRequestKind.Equipment
            && (Input.EquipmentAssetId is null || Input.EquipmentAssetId == Guid.Empty))
        {
            Error = "Выберите единицу оборудования.";
            return Page();
        }

        DateTimeOffset? specPlanned = null;
        if (Input.Kind == ServiceRequestKind.SpecTech)
        {
            if (!CanCreateTransport)
            {
                Error = "Нет права создавать транспортные заявки.";
                return Page();
            }

            if (Input.SpecialVehicleUnitId is null || Input.SpecialVehicleUnitId == Guid.Empty)
            {
                Error = "Выберите единицу спецтехники.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Input.SpecOperation))
            {
                Error = "Укажите операцию.";
                return Page();
            }

            if (!time.TryParseInput(Input.SpecPlannedAt, out var planned))
            {
                Error = "Укажите плановое время.";
                return Page();
            }

            specPlanned = planned;
        }

        var fromCatalog = string.Equals(Input.BodyMode, "catalog", StringComparison.OrdinalIgnoreCase);
        if (fromCatalog)
        {
            if (Input.CatalogPartId is null || Input.CatalogPartId == Guid.Empty)
            {
                Error = "Выберите услугу/узел из каталога исполнителя.";
                return Page();
            }

            var part = await api.GetAsync<CatalogPart>(
                $"api/catalog-parts/{Input.CatalogPartId}",
                HttpContext.RequestAborted);
            if (part.IsUnauthorized)
            {
                return Challenge();
            }

            if (!part.Ok || part.Data is null)
            {
                Error = part.Error ?? "Услуга/узел не найдены.";
                return Page();
            }

            if (part.Data.OwnerUserId != Input.ExecutorUserId.Value)
            {
                Error = "Услуга/узел не из каталога выбранного исполнителя.";
                return Page();
            }

            if (!part.Data.IsActive)
            {
                Error = "Услуга/узел неактивны.";
                return Page();
            }

            if (string.IsNullOrWhiteSpace(Input.Body))
            {
                Input.Body = string.IsNullOrWhiteSpace(part.Data.Description)
                    ? part.Data.Name
                    : $"{part.Data.Name}\n{part.Data.Description}";
            }
        }

        if (string.IsNullOrWhiteSpace(Input.Body))
        {
            Error = "Опишите заявку.";
            return Page();
        }

        var created = await api.PostAsync<ServiceRequestDetail>(
            "api/requests",
            new CreateServiceRequestRequest
            {
                Phone = Input.Phone.Trim(),
                DueAt = dueAt,
                InitiatorUserId = CanPickInitiator ? Input.InitiatorUserId : null,
                ExecutorUserId = Input.ExecutorUserId.Value,
                Body = Input.Body.Trim(),
                Kind = Input.Kind,
                IsUrgent = Input.IsUrgent,
                EquipmentAssetId = Input.Kind == ServiceRequestKind.Equipment ? Input.EquipmentAssetId : null,
                SpecialVehicleUnitId = Input.Kind == ServiceRequestKind.SpecTech ? Input.SpecialVehicleUnitId : null,
                SpecOperation = Input.Kind == ServiceRequestKind.SpecTech ? Input.SpecOperation?.Trim() : null,
                SpecPlannedAt = specPlanned
            },
            HttpContext.RequestAborted);

        if (created.IsUnauthorized)
        {
            return Challenge();
        }

        if (!created.Ok || created.Data is null)
        {
            Error = created.Error;
            return Page();
        }

        var failed = new List<string>();
        foreach (var file in Files.Where(f => f.Length > 0))
        {
            var upload = await api.UploadAsync($"api/requests/{created.Data.Id}/attachments", file, HttpContext.RequestAborted);
            if (!upload.Ok)
            {
                failed.Add($"{file.FileName}: {upload.Error}");
            }
        }

        TempData["Ok"] = $"Заявка № {created.Data.Id} подана.";
        if (failed.Count > 0)
        {
            TempData["Warn"] = "Не все файлы загружены. " + string.Join("; ", failed);
        }

        return Redirect($"/requests/{created.Data.Id}");
    }

    private string ResolveDisplayName() =>
        User.FindFirst(CrownAuthHandler.DisplayNameClaim)?.Value
        ?? User.Identity?.Name
        ?? string.Empty;

    private async Task<IActionResult?> LoadLookupsAsync()
    {
        var assignees = await api.GetAsync<List<RequestAssignee>>("api/requests/assignees", HttpContext.RequestAborted);
        if (assignees.IsUnauthorized)
        {
            return Challenge();
        }

        if (!assignees.Ok)
        {
            Error = assignees.Error;
            return null;
        }

        Assignees = assignees.Data ?? [];

        if (CanPickInitiator && Assignees.Count == 0)
        {
            Error ??= "Нет активных пользователей с ролью «Подразделение».";
        }

        var eq = await api.GetAsync<PagedResponse<EquipmentAsset>>(
            UiHelpers.BuildUrl("api/equipment", ("freeOnly", "true"), ("skip", "0"), ("take", "500")),
            HttpContext.RequestAborted);
        if (eq.Ok && eq.Data is not null)
        {
            FreeEquipment = eq.Data.Items;
        }

        var avail = await api.GetAsync<SpecialVehicleAvailabilityPayload>(
            "api/special-vehicles/availability",
            HttpContext.RequestAborted);
        if (avail.Ok && avail.Data is not null)
        {
            FreeSpecUnits = avail.Data.FreeUnits;
            SpecTypeAvailability = avail.Data.Types;
        }

        return null;
    }
}
