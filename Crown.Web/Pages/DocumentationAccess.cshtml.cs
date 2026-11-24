using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NoContent = Crown.Web.Services.NoContent;

namespace Crown.Web.Pages;

[Authorize(Roles = AppRoles.Administrator)]
public sealed class DocumentationAccessModel(CrownApiClient api) : PageModel
{
    public List<DocumentationLibrary> Libraries { get; private set; } = [];

    public List<DocumentationAclUser> Users { get; private set; } = [];

    /// <summary>Строки UI: цеха (бригады) + остальные пользователи поштучно.</summary>
    public List<AclRow> Rows { get; private set; } = [];

    public List<DocumentationLibraryMember> Members { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? LibraryId { get; set; }

    /// <summary>Форма: UserId отмеченных участников (не-бригады + развёрнутые цеха).</summary>
    [BindProperty]
    public List<Guid> MemberUserIds { get; set; } = [];

    /// <summary>Форма: UserId с правом записи.</summary>
    [BindProperty]
    public List<Guid> WriteUserIds { get; set; } = [];

    /// <summary>Ключи цехов с чтением (нормализованное имя цеха).</summary>
    [BindProperty]
    public List<string> MemberWorkshopKeys { get; set; } = [];

    /// <summary>Ключи цехов с записью.</summary>
    [BindProperty]
    public List<string> WriteWorkshopKeys { get; set; } = [];

    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        return await LoadAsync();
    }

    public async Task<IActionResult> OnPostSaveMembersAsync()
    {
        if (LibraryId is null)
        {
            Error = "Выберите каталог.";
            await LoadAsync();
            return Page();
        }

        // Сначала подгружаем пользователей, чтобы развернуть цеха → UserId.
        var usersResult = await api.GetAsync<List<DocumentationAclUser>>(
            "api/documentation/admin/users",
            HttpContext.RequestAborted);
        if (!usersResult.Ok || usersResult.Data is null)
        {
            Error = usersResult.Error ?? "Не удалось загрузить пользователей.";
            await LoadAsync();
            return Page();
        }

        Users = usersResult.Data;
        var workshopRead = MemberWorkshopKeys
            .Select(NormalizeWorkshop)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var workshopWrite = WriteWorkshopKeys
            .Select(NormalizeWorkshop)
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var fromWorkshops = Users
            .Where(u => IsBrigade(u)
                        && !string.IsNullOrWhiteSpace(u.BrigadeWorkshop)
                        && workshopRead.Contains(NormalizeWorkshop(u.BrigadeWorkshop)))
            .Select(u => u.UserId)
            .ToList();

        var writeFromWorkshops = Users
            .Where(u => IsBrigade(u)
                        && !string.IsNullOrWhiteSpace(u.BrigadeWorkshop)
                        && workshopWrite.Contains(NormalizeWorkshop(u.BrigadeWorkshop)))
            .Select(u => u.UserId)
            .ToHashSet();

        var memberIds = MemberUserIds
            .Concat(fromWorkshops)
            .Distinct()
            .ToList();

        var writeIds = WriteUserIds
            .Concat(writeFromWorkshops)
            .Where(memberIds.Contains)
            .Distinct()
            .ToHashSet();

        var members = memberIds
            .Select(id => new
            {
                UserId = id,
                CanWrite = writeIds.Contains(id)
            })
            .ToList();

        var result = await api.PutAsync<NoContent>(
            $"api/documentation/libraries/{LibraryId}/members",
            new { Members = members },
            HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error ?? "Не удалось сохранить участников.";
            await LoadAsync();
            return Page();
        }

        TempData["Ok"] = "Участники сохранены.";
        return RedirectToPage(new { LibraryId });
    }

    private async Task<IActionResult> LoadAsync()
    {
        var libraries = await api.GetAsync<List<DocumentationLibrary>>(
            "api/documentation/libraries",
            HttpContext.RequestAborted);
        if (libraries.IsUnauthorized)
        {
            return Challenge();
        }

        if (!libraries.Ok || libraries.Data is null)
        {
            Error = libraries.Error;
            return Page();
        }

        Libraries = libraries.Data;
        if (LibraryId is null && Libraries.Count > 0)
        {
            LibraryId = Libraries[0].Id;
        }

        var users = await api.GetAsync<List<DocumentationAclUser>>(
            "api/documentation/admin/users",
            HttpContext.RequestAborted);
        if (users.Ok && users.Data is not null)
        {
            Users = users.Data;
        }

        if (LibraryId is Guid lid)
        {
            var members = await api.GetAsync<List<DocumentationLibraryMember>>(
                $"api/documentation/libraries/{lid}/members",
                HttpContext.RequestAborted);
            if (members.Ok && members.Data is not null)
            {
                Members = members.Data;
            }
        }

        Rows = BuildRows(Users);
        return Page();
    }

    private static List<AclRow> BuildRows(List<DocumentationAclUser> users)
    {
        var rows = new List<AclRow>();

        var brigadesWithWorkshop = users
            .Where(u => IsBrigade(u) && !string.IsNullOrWhiteSpace(u.BrigadeWorkshop))
            .GroupBy(u => NormalizeWorkshop(u.BrigadeWorkshop), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

        foreach (var g in brigadesWithWorkshop)
        {
            var ids = g.Select(x => x.UserId).ToList();
            rows.Add(new AclRow
            {
                IsWorkshop = true,
                WorkshopKey = g.Key,
                Label = $"Цех {g.First().BrigadeWorkshop!.Trim()}",
                SubLabel = $"бригад: {ids.Count}",
                UserIds = ids
            });
        }

        // Не-бригады + бригады без цеха — поштучно.
        foreach (var u in users
                     .Where(u => !IsBrigade(u) || string.IsNullOrWhiteSpace(u.BrigadeWorkshop))
                     .OrderBy(u => u.DisplayName)
                     .ThenBy(u => u.UserName))
        {
            rows.Add(new AclRow
            {
                IsWorkshop = false,
                Label = u.DisplayName,
                SubLabel = u.UserName,
                UserIds = [u.UserId]
            });
        }

        return rows;
    }

    private static bool IsBrigade(DocumentationAclUser u) =>
        string.Equals(u.Role, AppRoles.Brigade, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeWorkshop(string? value) =>
        (value ?? string.Empty).Trim();

    public sealed class AclRow
    {
        public bool IsWorkshop { get; set; }
        public string? WorkshopKey { get; set; }
        public string Label { get; set; } = string.Empty;
        public string SubLabel { get; set; } = string.Empty;
        public List<Guid> UserIds { get; set; } = [];
    }
}
