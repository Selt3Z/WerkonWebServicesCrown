using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class TodayModel(CrownApiClient api) : PageModel
{
    public DateOnly Date { get; private set; }

    public List<ServiceRequestListItem> Incoming { get; private set; } = [];

    public List<ServiceRequestListItem> Outgoing { get; private set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        // Админу экран «Сегодня» не нужен — очередь личная по ролям исполнителя/инициатора.
        if (User.IsInRole(AppRoles.Administrator))
        {
            return Redirect("/");
        }

        var result = await api.GetAsync<TodayRequestsResponse>("api/requests/today", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            Error = result.Error ?? "Не удалось загрузить очередь на сегодня.";
            return Page();
        }

        Date = result.Data.Date;
        Incoming = result.Data.Incoming;
        Outgoing = result.Data.Outgoing;
        return Page();
    }
}
