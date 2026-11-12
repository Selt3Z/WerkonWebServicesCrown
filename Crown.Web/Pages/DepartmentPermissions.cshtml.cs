using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class DepartmentPermissionsModel(CrownApiClient api) : PageModel
{
    public List<DepartmentPermissionUser> Users { get; private set; } = [];

    [BindProperty]
    public List<DepartmentPermissionRowInput> Rows { get; set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var payload = new UpdateDepartmentPermissionsRequest
        {
            Users = Rows.Select(r => new DepartmentPermissionUserToggle
            {
                UserId = r.UserId,
                CanBeExecutor = r.CanBeExecutor,
                CanCreateTransportRequests = r.CanCreateTransportRequests,
                CanManageEquipment = r.CanManageEquipment,
                CanViewTransport = r.CanViewTransport,
                CanViewSpecialVehicles = r.CanViewSpecialVehicles,
                CanViewWarehouse = r.CanViewWarehouse,
                CanViewSummary = r.CanViewSummary,
                CanViewBrigadeRating = r.CanViewBrigadeRating,
                CanOperateBrigadeRating = r.CanOperateBrigadeRating,
                DocumentationEnabled = r.DocumentationEnabled
            }).ToList()
        };

        var result = await api.PutAsync<NoContent>(
            "api/department-permissions",
            payload,
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Права подразделений сохранены.";
        return RedirectToPage();
    }

    private async Task<IActionResult> LoadAsync()
    {
        var result = await api.GetAsync<List<DepartmentPermissionUser>>(
            "api/department-permissions",
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

        Users = result.Data;
        Rows = Users.Select(u => new DepartmentPermissionRowInput
        {
            UserId = u.UserId,
            CanBeExecutor = u.CanBeExecutor,
            CanCreateTransportRequests = u.CanCreateTransportRequests,
            CanManageEquipment = u.CanManageEquipment,
            CanViewTransport = u.CanViewTransport,
            CanViewSpecialVehicles = u.CanViewSpecialVehicles,
            CanViewWarehouse = u.CanViewWarehouse,
            CanViewSummary = u.CanViewSummary,
            CanViewBrigadeRating = u.CanViewBrigadeRating,
            CanOperateBrigadeRating = u.CanOperateBrigadeRating,
            DocumentationEnabled = u.DocumentationEnabled
        }).ToList();
        return Page();
    }

    public sealed class DepartmentPermissionRowInput
    {
        public Guid UserId { get; set; }
        public bool CanBeExecutor { get; set; }
        public bool CanCreateTransportRequests { get; set; }
        public bool CanManageEquipment { get; set; }
        public bool CanViewTransport { get; set; }
        public bool CanViewSpecialVehicles { get; set; }
        public bool CanViewWarehouse { get; set; }
        public bool CanViewSummary { get; set; }
        public bool CanViewBrigadeRating { get; set; }
        public bool CanOperateBrigadeRating { get; set; }
        public bool DocumentationEnabled { get; set; }
    }
}
