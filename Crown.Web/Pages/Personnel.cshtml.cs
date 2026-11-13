using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class PersonnelModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? State { get; set; }

    /// <summary>Для staff — чей персонал смотрим. Подразделение игнорирует (всегда свой).</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? OwnerUserId { get; set; }

    public List<PersonnelPerson> Items { get; private set; } = [];

    public List<RequestAssignee> Owners { get; private set; } = [];

    public bool HasMore { get; private set; }

    public string? Error { get; private set; }

    public bool IsStaff =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    public bool CanEdit => !IsStaff || OwnerUserId is Guid;

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsStaff)
        {
            await LoadOwnersAsync();
        }
        else
        {
            OwnerUserId = null; // API возьмёт текущего
        }

        var (items, hasMore, error, challenge) = await LoadAsync(0);
        if (challenge)
        {
            return Challenge();
        }

        Items = items;
        HasMore = hasMore;
        Error = error;
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
        return Partial("_PersonnelRows", items);
    }

    public async Task<IActionResult> OnGetMoreCardsAsync(int skip)
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
        return Partial("_PersonnelCards", items);
    }

    private string? EffectiveOwnerQuery() =>
        IsStaff ? OwnerUserId?.ToString() : null;

    private async Task LoadOwnersAsync()
    {
        var result = await api.GetAsync<List<RequestAssignee>>("api/requests/assignees", HttpContext.RequestAborted);
        if (result.Ok && result.Data is not null)
        {
            Owners = result.Data;
        }
    }

    private async Task<(List<PersonnelPerson> Items, bool HasMore, string? Error, bool Challenge)> LoadAsync(int skip)
    {
        if (IsStaff && OwnerUserId is null)
        {
            return ([], false, null, false);
        }

        var url = UiHelpers.BuildUrl(
            "api/personnel",
            ("ownerUserId", EffectiveOwnerQuery()),
            ("search", Q?.Trim()),
            ("activeFilter", State),
            ("skip", skip.ToString()),
            ("take", UiHelpers.PageSize.ToString()));

        var result = await api.GetAsync<PagedResponse<PersonnelPerson>>(url, HttpContext.RequestAborted);
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
