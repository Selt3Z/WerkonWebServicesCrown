using System.Globalization;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class BrigadeRatingStoppageModel(CrownApiClient api, AppTime time) : PageModel
{
    private bool CanEnter =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public StoppageFormInput Input { get; set; } = new();

    public List<BrigadeParticipant> Brigades { get; private set; } = [];

    /// <summary>Подразделения для «Кто остановил» (assignees = Department).</summary>
    public List<RequestAssignee> Departments { get; private set; } = [];

    public List<StoppageActItem> ActItems { get; private set; } = [];

    public List<StoppageWorkItem> WorkItems { get; private set; } = [];

    /// <summary>Персонал выбранной бригады (мастер).</summary>
    public List<PersonnelPerson> MasterPersonnel { get; private set; } = [];

    /// <summary>Персонал подразделения, которое остановило.</summary>
    public List<PersonnelPerson> StopperPersonnel { get; private set; } = [];

    public string? Error { get; private set; }

    public bool IsEdit => Id is Guid;

    public async Task<IActionResult> OnGetAsync()
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        await LoadLookupsAsync();
        if (Id is Guid id)
        {
            var result = await api.GetAsync<BrigadeStoppageDetail>(
                $"api/brigade-rating/stoppages/{id}",
                HttpContext.RequestAborted);
            if (result.IsUnauthorized)
            {
                return Challenge();
            }

            if (!result.Ok || result.Data is null)
            {
                Error = result.Error ?? "Остановка не найдена.";
                return Page();
            }

            Input = FromDetail(result.Data);
            EnsureCurrentBrigadeInList(result.Data.BrigadeUserId, result.Data.BrigadeName);
            await LoadWorkItemsAsync(Input.WorkItemId);
            await LoadMasterPersonnelAsync(Input.BrigadeUserId, Input.MasterPersonId);
            await LoadStopperPersonnelAsync(Input.StoppedByUserId, Input.StoppedByPersonId);
        }
        else
        {
            Input.ReportDate = time.GetToday();
            Input.StoppedAt = "08:00";
            Input.Remarks = [new RemarkLineInput { IsManual = false }, new RemarkLineInput { IsManual = false }, new RemarkLineInput { IsManual = false }];
            if (Brigades.Count == 1)
            {
                Input.BrigadeUserId = Brigades[0].Id;
                Input.MasterName = Brigades[0].Name;
            }

            await LoadWorkItemsAsync(null);
            await LoadMasterPersonnelAsync(Input.BrigadeUserId, null);
            await LoadStopperPersonnelAsync(Input.StoppedByUserId, null);
        }

        return Page();
    }

    /// <summary>Персонал владельца для JS (мастер или остановивший).</summary>
    public async Task<IActionResult> OnGetPersonnelAsync(Guid ownerUserId)
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        var people = await FetchPersonnelAsync(ownerUserId, null);
        return new JsonResult(people.Select(p => new
        {
            id = p.Id,
            fullName = p.FullName,
            position = p.Position
        }));
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        await LoadLookupsAsync();
        EnsureCurrentBrigadeInList(Input.BrigadeUserId, Input.BrigadeNameHint);
        await LoadWorkItemsAsync(Input.WorkItemId);
        await LoadMasterPersonnelAsync(Input.BrigadeUserId, Input.MasterPersonId);
        await LoadStopperPersonnelAsync(Input.StoppedByUserId, Input.StoppedByPersonId);

        if (Input.BrigadeUserId == Guid.Empty)
        {
            Error = "Выберите бригаду.";
            EnsureRemarkSlots();
            return Page();
        }

        if (Input.StoppedByUserId is null || Input.StoppedByUserId == Guid.Empty)
        {
            Error = "Выберите подразделение, которое остановило.";
            EnsureRemarkSlots();
            return Page();
        }

        if (!TryParseTime(Input.StoppedAt, out var stoppedAt))
        {
            Error = "Некорректное время остановки.";
            EnsureRemarkSlots();
            return Page();
        }

        TimeOnly? restartedAt = null;
        if (!string.IsNullOrWhiteSpace(Input.RestartedAt))
        {
            if (!TryParseTime(Input.RestartedAt, out var ra))
            {
                Error = "Некорректное время запуска.";
                EnsureRemarkSlots();
                return Page();
            }

            restartedAt = ra;
        }

        var remarks = (Input.Remarks ?? [])
            .Select(r =>
            {
                var isManual = r.IsManual || r.ActItemId is null || r.ActItemId == Guid.Empty;
                return new SaveStoppageRemarkRequest
                {
                    Id = r.Id,
                    ActItemId = isManual ? null : r.ActItemId,
                    Text = r.Text?.Trim() ?? string.Empty,
                    Comment = r.Comment?.Trim() ?? string.Empty,
                    IsManual = isManual,
                    IsResolved = r.IsResolved
                };
            })
            .Where(r => r.IsManual
                ? !string.IsNullOrWhiteSpace(r.Text)
                : r.ActItemId is Guid)
            .ToList();

        if (remarks.Count == 0)
        {
            Error = "Добавьте хотя бы один пункт акта.";
            EnsureRemarkSlots();
            return Page();
        }

        var workManual = Input.IsWorkManual || Input.WorkItemId is null || Input.WorkItemId == Guid.Empty;

        var payload = new SaveBrigadeStoppageRequest
        {
            BrigadeUserId = Input.BrigadeUserId,
            ReportDate = Input.ReportDate,
            MasterName = Input.MasterName,
            MasterPersonId = Input.MasterPersonId is Guid mpid && mpid != Guid.Empty ? mpid : null,
            StoppedByUserId = Input.StoppedByUserId,
            StoppedByPersonId = Input.StoppedByPersonId is Guid spid && spid != Guid.Empty ? spid : null,
            StoppedByName = Input.StoppedByName,
            StoppedAt = stoppedAt,
            RestartedAt = restartedAt,
            WorkDescription = Input.WorkDescription?.Trim() ?? string.Empty,
            WorkItemId = workManual ? null : Input.WorkItemId,
            IsWorkManual = workManual,
            Remarks = remarks
        };

        var result = Id is Guid id
            ? await api.PutAsync<BrigadeStoppageDetail>($"api/brigade-rating/stoppages/{id}", payload, HttpContext.RequestAborted)
            : await api.PostAsync<BrigadeStoppageDetail>("api/brigade-rating/stoppages", payload, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            Error = result.Error ?? "Не удалось сохранить остановку.";
            EnsureRemarkSlots();
            return Page();
        }

        TempData["Ok"] = IsEdit ? "Остановка обновлена." : "Остановка добавлена.";
        return RedirectToPage("/BrigadeRating");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        if (Id is not Guid id)
        {
            return RedirectToPage("/BrigadeRating");
        }

        var result = await api.DeleteAsync($"api/brigade-rating/stoppages/{id}", HttpContext.RequestAborted);
        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok)
        {
            await LoadLookupsAsync();
            await LoadWorkItemsAsync(Input.WorkItemId);
            await LoadMasterPersonnelAsync(Input.BrigadeUserId, Input.MasterPersonId);
            await LoadStopperPersonnelAsync(Input.StoppedByUserId, Input.StoppedByPersonId);
            Error = result.Error ?? "Не удалось удалить остановку.";
            EnsureRemarkSlots();
            return Page();
        }

        TempData["Ok"] = "Остановка удалена.";
        return RedirectToPage("/BrigadeRating");
    }

    private void EnsureRemarkSlots()
    {
        Input.Remarks ??= [];
        while (Input.Remarks.Count < 1)
        {
            Input.Remarks.Add(new RemarkLineInput());
        }
    }

    private void EnsureCurrentBrigadeInList(Guid brigadeUserId, string? name)
    {
        if (brigadeUserId == Guid.Empty)
        {
            return;
        }

        if (Brigades.Any(b => b.Id == brigadeUserId))
        {
            return;
        }

        Brigades.Insert(0, new BrigadeParticipant
        {
            Id = brigadeUserId,
            Name = string.IsNullOrWhiteSpace(name) ? "(бывший участник)" : name.Trim(),
            UserName = string.Empty
        });
    }

    private async Task LoadLookupsAsync()
    {
        var brigades = await api.GetAsync<List<BrigadeParticipant>>(
            "api/brigade-rating/brigades",
            HttpContext.RequestAborted);
        Brigades = brigades.Data ?? [];

        var depts = await api.GetAsync<List<RequestAssignee>>(
            "api/requests/assignees",
            HttpContext.RequestAborted);
        Departments = depts.Data ?? [];

        var acts = await api.GetAsync<List<StoppageActItem>>(
            "api/brigade-rating/act-items?activeOnly=true",
            HttpContext.RequestAborted);
        ActItems = acts.Data ?? [];
    }

    private async Task LoadWorkItemsAsync(Guid? currentWorkItemId)
    {
        var works = await api.GetAsync<List<StoppageWorkItem>>(
            "api/brigade-rating/work-items",
            HttpContext.RequestAborted);
        WorkItems = (works.Data ?? [])
            .Where(x => x.IsActive || (currentWorkItemId is Guid cid && x.Id == cid))
            .ToList();
    }

    private async Task LoadMasterPersonnelAsync(Guid brigadeUserId, Guid? currentMasterId) =>
        MasterPersonnel = await FetchPersonnelAsync(brigadeUserId, currentMasterId);

    private async Task LoadStopperPersonnelAsync(Guid? deptUserId, Guid? currentPersonId) =>
        StopperPersonnel = deptUserId is Guid id
            ? await FetchPersonnelAsync(id, currentPersonId)
            : [];

    private async Task<List<PersonnelPerson>> FetchPersonnelAsync(Guid ownerUserId, Guid? keepId)
    {
        if (ownerUserId == Guid.Empty)
        {
            return [];
        }

        var url = UiHelpers.BuildUrl(
            "api/personnel",
            ("ownerUserId", ownerUserId.ToString()),
            ("take", "500"));
        var result = await api.GetAsync<PagedResponse<PersonnelPerson>>(url, HttpContext.RequestAborted);
        return (result.Data?.Items ?? [])
            .Where(x => x.IsActive || (keepId is Guid kid && x.Id == kid))
            .ToList();
    }

    private static bool TryParseTime(string? raw, out TimeOnly value)
    {
        raw = raw?.Trim() ?? string.Empty;
        return TimeOnly.TryParseExact(raw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
            || TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static StoppageFormInput FromDetail(BrigadeStoppageDetail d) => new()
    {
        BrigadeUserId = d.BrigadeUserId,
        BrigadeNameHint = d.BrigadeName,
        ReportDate = d.ReportDate,
        MasterName = d.MasterName,
        MasterPersonId = d.MasterPersonId,
        StoppedByUserId = d.StoppedByUserId,
        StoppedByPersonId = d.StoppedByPersonId,
        StoppedByName = d.StoppedByName,
        StoppedAt = d.StoppedAt.ToString("HH:mm"),
        RestartedAt = d.RestartedAt?.ToString("HH:mm"),
        WorkDescription = d.WorkDescription,
        WorkItemId = d.WorkItemId,
        IsWorkManual = d.IsWorkManual || d.WorkItemId is null,
        Remarks = d.Remarks
            .OrderBy(r => r.SortOrder)
            .Select(r => new RemarkLineInput
            {
                Id = r.Id,
                ActItemId = r.ActItemId,
                Text = r.Text,
                Comment = r.Comment,
                IsManual = r.IsManual || r.ActItemId is null,
                IsResolved = r.IsResolved
            })
            .ToList()
    };

    public sealed class StoppageFormInput
    {
        public Guid BrigadeUserId { get; set; }
        public string? BrigadeNameHint { get; set; }
        public DateOnly ReportDate { get; set; }
        public string? MasterName { get; set; }
        public Guid? MasterPersonId { get; set; }
        public Guid? StoppedByUserId { get; set; }
        public Guid? StoppedByPersonId { get; set; }
        public string? StoppedByName { get; set; }
        public string? StoppedAt { get; set; }
        public string? RestartedAt { get; set; }
        public string? WorkDescription { get; set; }
        public Guid? WorkItemId { get; set; }
        public bool IsWorkManual { get; set; } = true;
        public List<RemarkLineInput> Remarks { get; set; } = [];
    }

    public sealed class RemarkLineInput
    {
        public Guid? Id { get; set; }
        public Guid? ActItemId { get; set; }
        public string? Text { get; set; }
        public string? Comment { get; set; }
        public bool IsManual { get; set; }
        public bool IsResolved { get; set; }
    }
}
