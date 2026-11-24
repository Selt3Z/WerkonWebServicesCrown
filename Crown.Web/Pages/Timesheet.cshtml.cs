using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>Кадровый табель — витрина UI. В БД ничего не пишем.</summary>
[Authorize]
public sealed class TimesheetModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public int Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int Month { get; set; }

    /// <summary>Staff: чей персонал в строках табеля.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? OwnerUserId { get; set; }

    public List<PersonnelPerson> People { get; private set; } = [];

    public List<RequestAssignee> Owners { get; private set; } = [];

    public string? Error { get; private set; }

    public bool IsStaff =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    public int DaysInMonth { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var today = DateTime.Today;
        if (Year is < 2000 or > 2100)
        {
            Year = today.Year;
        }

        if (Month is < 1 or > 12)
        {
            Month = today.Month;
        }

        DaysInMonth = DateTime.DaysInMonth(Year, Month);

        if (IsStaff)
        {
            var owners = await api.GetAsync<List<RequestAssignee>>(
                "api/personnel/owners",
                HttpContext.RequestAborted);
            if (owners.IsUnauthorized)
            {
                return Challenge();
            }

            if (owners.Ok && owners.Data is not null)
            {
                Owners = owners.Data;
            }

            if (OwnerUserId is null)
            {
                return Page();
            }
        }

        var url = UiHelpers.BuildUrl(
            "api/personnel",
            ("ownerUserId", IsStaff ? OwnerUserId?.ToString() : null),
            ("activeFilter", "active"),
            ("skip", "0"),
            ("take", "200"));

        var people = await api.GetAsync<PagedResponse<PersonnelPerson>>(url, HttpContext.RequestAborted);
        if (people.IsUnauthorized)
        {
            return Challenge();
        }

        if (!people.Ok || people.Data is null)
        {
            Error = people.Error;
            return Page();
        }

        People = people.Data.Items
            .OrderBy(x => x.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return Page();
    }
}
