using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Staff)]
public sealed class HistoryModel(CrownApiClient api) : PageModel
{
    public const string ClearConfirmationWord = "ОЧИСТИТЬ";

    [BindProperty(SupportsGet = true)]
    public Guid? UserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? EntityType { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    public List<AuditLogItem> Items { get; private set; } = [];

    public List<AuditLogUserOption> Users { get; private set; } = [];

    /// <summary>Типы сущностей в журнале (как AuditEntityTypes на API).</summary>
    public IReadOnlyList<(string Value, string Label)> EntityTypeOptions { get; } =
    [
        ("ServiceRequest", "Заявка"),
        ("CatalogPart", "Каталог услуг/узлов"),
        ("WarehousePart", "Склад"),
        ("EquipmentAsset", "Оборудование"),
        ("SpecialVehicleUnit", "Спецтехника"),
        ("Vehicle", "Транспорт"),
        ("ParkingLot", "Стоянка"),
        ("VehicleTrip", "Поездка"),
        ("VehicleFuelRefuel", "Заправка"),
        ("VehicleRepair", "Ремонт"),
        ("Documentation", "Документация"),
        ("BrigadeStoppage", "Остановка бригады"),
        ("StoppageActItem", "Пункт акта"),
        ("StoppageWorkItem", "Выполняемая работа"),
        ("PersonnelPerson", "Сотрудник"),
        ("AppSetting", "Настройки"),
        ("User", "Пользователь"),
        ("AuditLog", "Журнал")
    ];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public bool CanClear => User.IsInRole(AppRoles.Administrator);

    [BindProperty]
    public string? ClearConfirmation { get; set; }

    [BindProperty]
    public string? ClearPassword { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var (items, hasMore, error, challenge) = await LoadAsync(0);
        if (challenge)
        {
            return Challenge();
        }

        Items = items;
        HasMore = hasMore;
        Error = error;

        var users = await api.GetAsync<List<AuditLogUserOption>>("api/audit-log/users", HttpContext.RequestAborted);
        Users = users.Data ?? [];
        return Page();
    }

    public async Task<IActionResult> OnGetMoreAsync(int skip)
    {
        if (skip < 0)
        {
            skip = 0;
        }

        var (items, hasMore, _, challenge) = await LoadAsync(skip);
        if (challenge)
        {
            return Challenge();
        }

        Response.Headers["X-Has-More"] = hasMore ? "true" : "false";
        return Partial("_HistoryRows", items);
    }

    public async Task<IActionResult> OnPostClearAsync()
    {
        if (!CanClear)
        {
            return Forbid();
        }

        var result = await api.PostAsync<ClearAuditLogResult>(
            "api/audit-log/clear",
            new ClearAuditLogRequest
            {
                Confirmation = ClearConfirmation,
                Password = ClearPassword
            },
            HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            TempData["Warn"] = result.Error ?? "Не удалось очистить журнал.";
            return RedirectToPage();
        }

        TempData["Ok"] = $"Журнал очищен. Удалено записей: {result.Data.Deleted}.";
        return RedirectToPage();
    }

    private async Task<(List<AuditLogItem> Items, bool HasMore, string? Error, bool Challenge)> LoadAsync(int skip)
    {
        var url = UiHelpers.BuildUrl(
            "api/audit-log",
            ("userId", UserId?.ToString()),
            ("entityType", EntityType?.Trim()),
            ("dateFrom", From),
            ("dateTo", To),
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<AuditLogItem>>(url, HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return ([], false, null, true);
        }

        if (!result.Ok || result.Data is null)
        {
            return ([], false, result.Error, false);
        }

        return (result.Data.Items, result.Data.HasMore, null, false);
    }
}
