using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class PersonnelSaveModel(CrownApiClient api) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public Guid? OwnerUserId { get; set; }

    [BindProperty]
    public SavePersonnelPersonRequest Input { get; set; } = new() { IsActive = true };

    public List<RequestAssignee> Owners { get; private set; } = [];

    public string? Error { get; private set; }

    public bool IsEdit => Id is Guid;

    public bool IsStaff =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    public async Task<IActionResult> OnGetAsync()
    {
        if (IsStaff)
        {
            await LoadOwnersAsync();
            if (Id is null && OwnerUserId is null)
            {
                Error = "Выберите подразделение на странице персонала.";
                return Page();
            }
        }

        if (Id is Guid id)
        {
            var person = await api.GetAsync<PersonnelPerson>($"api/personnel/{id}", HttpContext.RequestAborted);
            if (person.IsUnauthorized)
            {
                return Challenge();
            }

            if (!person.Ok || person.Data is null)
            {
                Error = person.Error ?? "Сотрудник не найден.";
                return Page();
            }

            OwnerUserId = person.Data.OwnerUserId;
            Input = new SavePersonnelPersonRequest
            {
                OwnerUserId = person.Data.OwnerUserId,
                FullName = person.Data.FullName,
                Position = person.Data.Position,
                IsActive = person.Data.IsActive
            };
        }
        else if (IsStaff)
        {
            Input.OwnerUserId = OwnerUserId;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (IsStaff)
        {
            await LoadOwnersAsync();
            Input.OwnerUserId ??= OwnerUserId;
            if (Input.OwnerUserId is null || Input.OwnerUserId == Guid.Empty)
            {
                Error = "Укажите подразделение-владельца персонала.";
                return Page();
            }
        }

        if (string.IsNullOrWhiteSpace(Input.FullName))
        {
            Error = "ФИО обязательно.";
            return Page();
        }

        var result = Id is Guid id
            ? await api.PutAsync<PersonnelPerson>($"api/personnel/{id}", Input, HttpContext.RequestAborted)
            : await api.PostAsync<PersonnelPerson>("api/personnel", Input, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error;
            return Page();
        }

        TempData["Ok"] = IsEdit ? "Сотрудник обновлён." : "Сотрудник добавлен.";
        return RedirectBack();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (Id is not Guid id)
        {
            return RedirectToPage("/Personnel");
        }

        if (IsStaff)
        {
            await LoadOwnersAsync();
        }

        var result = await api.DeleteAsync($"api/personnel/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error ?? "Не удалось удалить.";
            return Page();
        }

        TempData["Ok"] = "Сотрудник удалён.";
        return RedirectBack();
    }

    private IActionResult RedirectBack()
    {
        var backOwner = Input.OwnerUserId ?? OwnerUserId;
        return IsStaff && backOwner is Guid oid
            ? Redirect($"/personnel?ownerUserId={oid}")
            : RedirectToPage("/Personnel");
    }

    private async Task LoadOwnersAsync()
    {
        var result = await api.GetAsync<List<RequestAssignee>>("api/requests/assignees", HttpContext.RequestAborted);
        if (result.Ok && result.Data is not null)
        {
            Owners = result.Data;
        }
    }
}
