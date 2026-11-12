using System.Globalization;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

[Authorize]
public sealed class BrigadeRatingStoppageModel(CrownApiClient api) : PageModel
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

    public List<RequestAssignee> Users { get; private set; } = [];

    public List<StoppageActItem> ActItems { get; private set; } = [];

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
            // Если у текущей бригады забрали право — всё равно показываем её в селекте.
            EnsureCurrentBrigadeInList(result.Data.BrigadeUserId, result.Data.BrigadeName);
        }
        else
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            Input.ReportDate = today;
            Input.StoppedAt = "08:00";
            Input.Remarks = [new RemarkLineInput { IsManual = false }, new RemarkLineInput { IsManual = false }, new RemarkLineInput { IsManual = false }];
            if (Brigades.Count == 1)
            {
                Input.BrigadeUserId = Brigades[0].Id;
                Input.MasterName = Brigades[0].Name;
            }
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        await LoadLookupsAsync();
        EnsureCurrentBrigadeInList(Input.BrigadeUserId, Input.BrigadeNameHint);

        if (Input.BrigadeUserId == Guid.Empty)
        {
            Error = "Выберите бригаду.";
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

        var payload = new SaveBrigadeStoppageRequest
        {
            BrigadeUserId = Input.BrigadeUserId,
            ReportDate = Input.ReportDate,
            MasterName = Input.MasterName,
            StoppedByUserId = Input.StoppedByUserId,
            StoppedByName = Input.StoppedByName,
            StoppedAt = stoppedAt,
            RestartedAt = restartedAt,
            WorkDescription = Input.WorkDescription?.Trim() ?? string.Empty,
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

        var users = await api.GetAsync<List<RequestAssignee>>(
            "api/requests/assignees",
            HttpContext.RequestAborted);
        Users = users.Data ?? [];

        var acts = await api.GetAsync<List<StoppageActItem>>(
            "api/brigade-rating/act-items?activeOnly=true",
            HttpContext.RequestAborted);
        ActItems = acts.Data ?? [];
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
        StoppedByUserId = d.StoppedByUserId,
        StoppedByName = d.StoppedByName,
        StoppedAt = d.StoppedAt.ToString("HH:mm"),
        RestartedAt = d.RestartedAt?.ToString("HH:mm"),
        WorkDescription = d.WorkDescription,
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
        /// <summary>Имя бригады из detail — если её уже нет в списке участников.</summary>
        public string? BrigadeNameHint { get; set; }
        public DateOnly ReportDate { get; set; }
        public string? MasterName { get; set; }
        public Guid? StoppedByUserId { get; set; }
        public string? StoppedByName { get; set; }
        public string? StoppedAt { get; set; }
        public string? RestartedAt { get; set; }
        public string? WorkDescription { get; set; }
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
