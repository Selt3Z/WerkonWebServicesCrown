using System.Globalization;
using Crown.Web.Models;
using Crown.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Crown.Web.Pages;

/// <summary>
/// Форма «Добавить» рейтинга бригад: два режима — остановка и проверка (надзорная / ПДК / ПБиОТ).
/// Режим и вид при правке заблокированы (определяются по id, а не по форме).
/// </summary>
[Authorize]
public sealed class BrigadeRatingStoppageModel(CrownApiClient api, AppTime time) : PageModel
{
    /// <summary>Значение option «Добавить» в селектах персонала (показывает поля Должность + ФИО).</summary>
    public const string AddValue = "__add__";

    private const string MasterField = "Input.MasterPersonId";
    private const string StopperField = "Input.StoppedByPersonId";
    private const string CheckerField = "Input.CheckerPersonId";

    public enum EntryMode
    {
        Stoppage = 0,
        Inspection = 1
    }

    private bool CanEnter =>
        User.IsInRole(AppRoles.Administrator)
        || User.IsInRole(AppRoles.Dispatcher)
        || User.FindFirst(CrownAuthHandler.CanOperateBrigadeRatingClaim)?.Value == "1";

    /// <summary>
    /// POST api/personnel пишет в список указанного владельца только для staff
    /// (иначе API молча создаст человека в личном списке оператора).
    /// </summary>
    private bool CanCreatePersonnel =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    /// <summary>Id остановки (route) — правка остановки.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    /// <summary>Id проверки (query / скрытое поле формы) — правка проверки.</summary>
    [BindProperty(SupportsGet = true)]
    public Guid? InspectionId { get; set; }

    /// <summary>Вид проверки из ссылки («+» на странице проверок): Supervising | Pdk | Pbiot или 1..3.</summary>
    [BindProperty(SupportsGet = true)]
    public string? InspectionKind { get; set; }

    [BindProperty]
    public EntryFormInput Input { get; set; } = new();

    public List<BrigadeParticipant> Brigades { get; private set; } = [];

    /// <summary>Подразделения для «Кто остановил» и проверяющего (assignees = Department).</summary>
    public List<RequestAssignee> Departments { get; private set; } = [];

    public List<StoppageActItem> ActItems { get; private set; } = [];

    public List<StoppageWorkItem> WorkItems { get; private set; } = [];

    /// <summary>Персонал выбранной бригады (мастер).</summary>
    public List<PersonnelPerson> MasterPersonnel { get; private set; } = [];

    /// <summary>Персонал подразделения, которое остановило.</summary>
    public List<PersonnelPerson> StopperPersonnel { get; private set; } = [];

    /// <summary>Персонал подразделения проверяющего.</summary>
    public List<PersonnelPerson> CheckerPersonnel { get; private set; } = [];

    /// <summary>Сотрудники бригад цеха — «ответственный за устранение» (ПБиОТ).</summary>
    public List<WorkshopPersonnelItem> WorkshopPersonnel { get; private set; } = [];

    public string? Error { get; private set; }

    /// <summary>Поля селектов персонала, в которых сейчас выбран пункт «Добавить» (после ошибки сервера).</summary>
    private readonly HashSet<string> _addPending = [];

    public bool IsAddPending(string field) => _addPending.Contains(field);

    /// <summary>Сообщение об успешном сохранении (форма остаётся заполненной).</summary>
    public string? Success { get; private set; }

    /// <summary>URL режима правки после сохранения — JS подменяет адрес через history.replaceState.</summary>
    public string? SavedUrl { get; private set; }

    public bool IsEdit => Id is not null || InspectionId is not null;

    /// <summary>Режим: при правке — по id, при создании — из радио формы.</summary>
    public EntryMode ActiveMode =>
        InspectionId is not null ? EntryMode.Inspection
        : Id is not null ? EntryMode.Stoppage
        : Input.Mode;

    public bool IsInspection => ActiveMode == EntryMode.Inspection;

    public string PageTitle =>
        !IsEdit ? "Добавить" : IsInspection ? "Правка проверки" : "Правка остановки";

    public string CancelUrl => IsInspection
        ? BrigadeRatingInspectionsModel.ListPath(Input.Kind)
        : "/brigade-rating";

    /// <summary>Цех выбранной бригады (readonly-поле «Цех»).</summary>
    public string CurrentWorkshop =>
        Brigades.FirstOrDefault(b => b.Id == Input.BrigadeUserId)?.Workshop ?? string.Empty;

    public string TodayIso => time.GetToday().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // ---------- GET ----------

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
                ApplyNewDefaults();
                return await RenderAsync();
            }

            Input = FromDetail(result.Data);
            EnsureCurrentBrigadeInList(result.Data.BrigadeUserId, result.Data.BrigadeName, null);
        }
        else if (InspectionId is Guid iid)
        {
            var result = await api.GetAsync<BrigadeInspectionDetail>(
                $"api/brigade-rating/inspections/{iid}",
                HttpContext.RequestAborted);
            if (result.IsUnauthorized)
            {
                return Challenge();
            }

            if (!result.Ok || result.Data is null)
            {
                Error = result.Error ?? "Проверка не найдена.";
                InspectionId = null;
                ApplyNewDefaults();
                return await RenderAsync();
            }

            Input = FromInspectionDetail(result.Data);
            EnsureCurrentBrigadeInList(result.Data.BrigadeUserId, Input.BrigadeNameHint, result.Data.Workshop);
        }
        else
        {
            ApplyNewDefaults();
        }

        return await RenderAsync();
    }

    /// <summary>Персонал владельца для JS (мастер, остановивший или проверяющий).</summary>
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

    /// <summary>Сотрудники бригад цеха для JS (ПБиОТ: ответственный за устранение).</summary>
    public async Task<IActionResult> OnGetWorkshopPersonnelAsync(string? workshop)
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        var people = await FetchWorkshopPersonnelAsync(workshop);
        return new JsonResult(people.Select(p => new
        {
            id = p.Id,
            fullName = p.FullName,
            position = p.Position,
            brigadeName = p.BrigadeName
        }));
    }

    // ---------- POST ----------

    public async Task<IActionResult> OnPostAsync()
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        // При правке режим диктует id (радио заблокированы), а не поле формы.
        Input.Mode = ActiveMode;

        // Запоминаем, где в селектах остался пункт «Добавить» — чтобы после ошибки показать его снова.
        foreach (var field in new[] { MasterField, StopperField, CheckerField })
        {
            if (IsAddSelected(field))
            {
                _addPending.Add(field);
            }
        }

        return Input.Mode == EntryMode.Inspection
            ? await SaveInspectionAsync()
            : await SaveStoppageAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        if (InspectionId is Guid iid)
        {
            var del = await api.DeleteAsync($"api/brigade-rating/inspections/{iid}", HttpContext.RequestAborted);
            if (del.IsUnauthorized)
            {
                return Challenge();
            }

            if (!del.Ok)
            {
                Input.Mode = EntryMode.Inspection;
                return await FailAsync(del.Error ?? "Не удалось удалить проверку.");
            }

            TempData["Ok"] = "Проверка удалена.";
            return Redirect(BrigadeRatingInspectionsModel.ListPath(Input.Kind));
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
            Input.Mode = EntryMode.Stoppage;
            return await FailAsync(result.Error ?? "Не удалось удалить остановку.");
        }

        TempData["Ok"] = "Остановка удалена.";
        return RedirectToPage("/BrigadeRating");
    }

    // ---------- Сохранение остановки ----------

    private async Task<IActionResult> SaveStoppageAsync()
    {
        if (Input.BrigadeUserId == Guid.Empty)
        {
            return await FailAsync("Выберите бригаду.");
        }

        if (Input.StoppedByUserId is null || Input.StoppedByUserId == Guid.Empty)
        {
            return await FailAsync("Выберите подразделение, которое остановило.");
        }

        if (!TryParseTime(Input.StoppedAt, out var stoppedAt))
        {
            return await FailAsync("Некорректное время остановки.");
        }

        TimeOnly? restartedAt = null;
        if (!string.IsNullOrWhiteSpace(Input.RestartedAt))
        {
            if (!TryParseTime(Input.RestartedAt, out var ra))
            {
                return await FailAsync("Некорректное время запуска.");
            }

            restartedAt = ra;
        }

        var remarks = Input.Remarks.Select(ToRemarkRequest)
            .Where(r => r.IsManual ? !string.IsNullOrWhiteSpace(r.Text) : r.ActItemId is Guid)
            .Select(r => new SaveStoppageRemarkRequest
            {
                Id = r.Id,
                ActItemId = r.ActItemId,
                Text = r.Text ?? string.Empty,
                Comment = r.Comment ?? string.Empty,
                IsManual = r.IsManual,
                IsResolved = r.IsResolved
            })
            .ToList();
        if (remarks.Count == 0)
        {
            return await FailAsync("Добавьте хотя бы один пункт акта.");
        }

        // Все проверки прошли — только теперь создаём персонал (чтобы не плодить людей при отказе по валидации).
        var masterAdd = IsAddSelected(MasterField);
        var stopperAdd = IsAddSelected(StopperField);
        var masterNewName = Clean(Input.NewMasterFullName);
        var stopperNewName = Clean(Input.NewStopperFullName);
        if ((masterAdd && masterNewName is not null) || (stopperAdd && stopperNewName is not null))
        {
            if (!CanCreatePersonnel)
            {
                return await FailAsync("Добавлять новых сотрудников может только администратор или диспетчер.");
            }
        }

        if (masterAdd)
        {
            if (masterNewName is not null)
            {
                var created = await FindOrCreatePersonAsync(Input.BrigadeUserId, masterNewName, Clean(Input.NewMasterPosition));
                if (created.IsUnauthorized)
                {
                    return Challenge();
                }

                if (!created.Ok || created.Data is null)
                {
                    return await FailAsync(created.Error ?? "Не удалось добавить мастера.");
                }

                Input.MasterPersonId = created.Data.Id;
                Input.MasterName = created.Data.FullName;
                _addPending.Remove(MasterField); // человек создан — дальше в селекте он, а не «Добавить»
            }
            else
            {
                Input.MasterPersonId = null; // «Добавить» выбрано, но ФИО не введено — мастер без привязки.
            }

            ResetKeys(MasterField, "Input.NewMasterFullName", "Input.NewMasterPosition", "Input.MasterName");
            Input.NewMasterFullName = null;
            Input.NewMasterPosition = null;
        }

        if (stopperAdd)
        {
            if (stopperNewName is not null)
            {
                var created = await FindOrCreatePersonAsync(
                    Input.StoppedByUserId.Value, stopperNewName, Clean(Input.NewStopperPosition));
                if (created.IsUnauthorized)
                {
                    return Challenge();
                }

                if (!created.Ok || created.Data is null)
                {
                    return await FailAsync(created.Error ?? "Не удалось добавить сотрудника.");
                }

                Input.StoppedByPersonId = created.Data.Id;
                Input.StoppedByName = created.Data.FullName;
                _addPending.Remove(StopperField);
            }
            else
            {
                Input.StoppedByPersonId = null;
            }

            ResetKeys(StopperField, "Input.NewStopperFullName", "Input.NewStopperPosition", "Input.StoppedByName");
            Input.NewStopperFullName = null;
            Input.NewStopperPosition = null;
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

        var wasEdit = Id is not null;
        var result = Id is Guid id
            ? await api.PutAsync<BrigadeStoppageDetail>($"api/brigade-rating/stoppages/{id}", payload, HttpContext.RequestAborted)
            : await api.PostAsync<BrigadeStoppageDetail>("api/brigade-rating/stoppages", payload, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return await FailAsync(result.Error ?? "Не удалось сохранить остановку.");
        }

        // Форма не очищается: перечитываем сохранённое состояние и переходим в режим правки,
        // иначе повторное «Сохранить» создало бы дубль.
        Input = FromDetail(result.Data);
        Id = result.Data.Id;
        InspectionId = null;
        ModelState.Clear();
        _addPending.Clear();
        Success = wasEdit ? "Остановка обновлена." : "Остановка добавлена.";
        SavedUrl = $"/brigade-rating/stoppage/{result.Data.Id}";
        return await RenderAsync();
    }

    // ---------- Сохранение проверки ----------

    private async Task<IActionResult> SaveInspectionAsync()
    {
        if (!Enum.IsDefined(Input.Kind))
        {
            return await FailAsync("Не указан вид проверки.");
        }

        if (Input.BrigadeUserId == Guid.Empty)
        {
            return await FailAsync("Выберите бригаду.");
        }

        if (Input.ReportDate == default)
        {
            return await FailAsync("Укажите дату.");
        }

        var remarks = Input.Remarks.Select(ToRemarkRequest)
            .Where(r => r.IsManual ? !string.IsNullOrWhiteSpace(r.Text) : r.ActItemId is Guid)
            .Select(r => new SaveInspectionRemarkRequest
            {
                ActItemId = r.ActItemId,
                Text = r.Text ?? string.Empty,
                Comment = r.Comment,
                IsManual = r.IsManual
            })
            .ToList();
        if (remarks.Count == 0)
        {
            return await FailAsync("Добавьте хотя бы одно нарушение.");
        }

        // ----- Мастер: выбранный / новый (создаст API вместе с проверкой) / текст без привязки -----
        var masterAdd = IsAddSelected(MasterField);
        var masterNewName = masterAdd ? Clean(Input.NewMasterFullName) : null;
        Guid? masterPersonId = !masterAdd && Input.MasterPersonId is Guid mp && mp != Guid.Empty ? mp : null;
        var masterName = masterPersonId is null && masterNewName is null ? Clean(Input.MasterName) : null;
        if (Input.Kind == BrigadeInspectionKind.Supervising
            && masterPersonId is null && masterNewName is null && masterName is null)
        {
            return await FailAsync("Укажите мастера.");
        }

        // ----- Проверяющий: подразделение + сотрудник (или «Добавить») -----
        if (Input.DepartmentUserId is null || Input.DepartmentUserId == Guid.Empty)
        {
            return await FailAsync("Выберите подразделение проверяющего.");
        }

        var checkerAdd = IsAddSelected(CheckerField);
        var checkerNewName = checkerAdd ? Clean(Input.NewCheckerFullName) : null;
        Guid? checkerPersonId = !checkerAdd && Input.CheckerPersonId is Guid cp && cp != Guid.Empty ? cp : null;
        var checkerName = checkerPersonId is null && checkerNewName is null ? Clean(Input.CheckerName) : null;
        if (checkerPersonId is null && checkerNewName is null && checkerName is null)
        {
            return await FailAsync("Укажите проверяющего.");
        }

        if (Input.Kind == BrigadeInspectionKind.Supervising && Input.Outcome is null)
        {
            return await FailAsync("Укажите: предписание, замечание или остановка.");
        }

        var payload = new SaveBrigadeInspectionRequest
        {
            Kind = Input.Kind,
            ReportDate = Input.ReportDate,
            BrigadeUserId = Input.BrigadeUserId,
            MasterPersonId = masterPersonId,
            MasterName = masterName,
            NewMasterFullName = masterNewName,
            NewMasterPosition = masterNewName is null ? null : Clean(Input.NewMasterPosition),
            Well = Input.Kind == BrigadeInspectionKind.Pdk ? null : Clean(Input.Well),
            DepartmentUserId = Input.DepartmentUserId,
            CheckerPersonId = checkerPersonId,
            CheckerName = checkerName,
            CheckerPosition = checkerName is null ? null : Clean(Input.CheckerPosition),
            NewCheckerFullName = checkerNewName,
            NewCheckerPosition = checkerNewName is null ? null : Clean(Input.NewCheckerPosition),
            FixDateIsManual = Input.FixDateIsManual,
            FixDate = Input.FixDateIsManual ? null : Input.FixDate,
            FixDateManualText = Input.FixDateIsManual ? Clean(Input.FixDateManualText) : null,
            Remarks = remarks
        };

        // Поля, специфичные для вида (API всё равно обнуляет чужие).
        switch (Input.Kind)
        {
            case BrigadeInspectionKind.Supervising:
                payload.Outcome = Input.Outcome;
                payload.StopPoint = Input.StopPoint;
                payload.HasExplanation = Input.HasExplanation;
                break;
            case BrigadeInspectionKind.Pdk:
                payload.FixResponsibleText = Clean(Input.FixResponsibleText);
                break;
            case BrigadeInspectionKind.Pbiot:
                payload.FixReportText = Clean(Input.FixReportText);
                if (Input.FixResponsiblePersonId is Guid frid && frid != Guid.Empty)
                {
                    payload.FixResponsiblePersonId = frid;
                }
                else
                {
                    payload.FixResponsibleName = Clean(Input.FixResponsibleName);
                }

                break;
        }

        var wasEdit = InspectionId is not null;
        var result = InspectionId is Guid id
            ? await api.PutAsync<BrigadeInspectionDetail>($"api/brigade-rating/inspections/{id}", payload, HttpContext.RequestAborted)
            : await api.PostAsync<BrigadeInspectionDetail>("api/brigade-rating/inspections", payload, HttpContext.RequestAborted);

        if (result.IsUnauthorized)
        {
            return Challenge();
        }

        if (!result.Ok || result.Data is null)
        {
            return await FailAsync(result.Error ?? "Не удалось сохранить проверку.");
        }

        // Форма не очищается: подхватываем сохранённое (в т.ч. id новых сотрудников) и переходим в режим правки.
        Input = FromInspectionDetail(result.Data);
        InspectionId = result.Data.Id;
        Id = null;
        ModelState.Clear();
        _addPending.Clear();
        Success = wasEdit ? "Проверка обновлена." : "Проверка добавлена.";
        SavedUrl = $"/brigade-rating/stoppage?inspectionId={result.Data.Id}";
        return await RenderAsync();
    }

    // ---------- Общие вспомогательные ----------

    /// <summary>Ошибка валидации/API: показываем форму как была (с перезагруженными справочниками).</summary>
    private async Task<IActionResult> FailAsync(string message)
    {
        Error = message;
        return await RenderAsync();
    }

    /// <summary>Загрузка справочников и зависимых списков под текущий Input, затем отрисовка страницы.</summary>
    private async Task<IActionResult> RenderAsync()
    {
        if (Brigades.Count == 0 && Departments.Count == 0)
        {
            await LoadLookupsAsync();
        }

        EnsureCurrentBrigadeInList(Input.BrigadeUserId, Input.BrigadeNameHint, null);
        EnsureRemarkSlots();
        await LoadDependentAsync();
        await LoadCatalogSelectionsAsync();
        return Page();
    }

    /// <summary>AJAX-поиск по справочнику act/work (не отдаём 6k строк в HTML).</summary>
    public async Task<IActionResult> OnGetCatalogSearchAsync(string kind = "act", string? q = null, int take = 40)
    {
        if (!CanEnter)
        {
            return Forbid();
        }

        take = Math.Clamp(take, 1, 100);
        var path = string.Equals(kind, "work", StringComparison.OrdinalIgnoreCase) ? "work-items" : "act-items";
        var url = $"api/brigade-rating/{path}?activeOnly=true&take={take}&skip=0";
        if (!string.IsNullOrWhiteSpace(q))
        {
            url += "&q=" + Uri.EscapeDataString(q.Trim());
        }

        if (path == "act-items")
        {
            var result = await api.GetAsync<StoppageActItemsList>(url, HttpContext.RequestAborted);
            if (result.IsUnauthorized)
            {
                return Challenge();
            }

            if (!result.Ok)
            {
                return StatusCode(result.Status > 0 ? result.Status : 502);
            }

            var items = (result.Data?.Items ?? []).Select(x => new { id = x.Id, text = x.Text });
            return new JsonResult(items);
        }
        else
        {
            var result = await api.GetAsync<StoppageWorkItemsList>(url, HttpContext.RequestAborted);
            if (result.IsUnauthorized)
            {
                return Challenge();
            }

            if (!result.Ok)
            {
                return StatusCode(result.Status > 0 ? result.Status : 502);
            }

            var items = (result.Data?.Items ?? []).Select(x => new { id = x.Id, text = x.Text });
            return new JsonResult(items);
        }
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
    }

    private async Task LoadDependentAsync()
    {
        MasterPersonnel = await FetchPersonnelAsync(Input.BrigadeUserId, Input.MasterPersonId);
        StopperPersonnel = Input.StoppedByUserId is Guid sid
            ? await FetchPersonnelAsync(sid, Input.StoppedByPersonId)
            : [];
        CheckerPersonnel = Input.DepartmentUserId is Guid did
            ? await FetchPersonnelAsync(did, Input.CheckerPersonId)
            : [];

        // Список ответственных нужен только ПБиОТ — остальным лишний запрос не делаем.
        WorkshopPersonnel = IsInspection && Input.Kind == BrigadeInspectionKind.Pbiot
            ? await FetchWorkshopPersonnelAsync(CurrentWorkshop)
            : [];
    }

    /// <summary>Только уже выбранные пункты/работы — для отображения в форме.</summary>
    private async Task LoadCatalogSelectionsAsync()
    {
        var actIds = Input.Remarks
            .Where(r => r.ActItemId is Guid g && g != Guid.Empty)
            .Select(r => r.ActItemId!.Value)
            .Distinct()
            .Take(100)
            .ToList();
        if (actIds.Count > 0)
        {
            var acts = await api.GetAsync<StoppageActItemsList>(
                "api/brigade-rating/act-items?ids=" + string.Join(",", actIds),
                HttpContext.RequestAborted);
            ActItems = acts.Data?.Items ?? [];
        }
        else
        {
            ActItems = [];
        }

        if (Input.WorkItemId is Guid wid && wid != Guid.Empty)
        {
            var works = await api.GetAsync<StoppageWorkItemsList>(
                "api/brigade-rating/work-items?ids=" + wid,
                HttpContext.RequestAborted);
            WorkItems = works.Data?.Items ?? [];
        }
        else
        {
            WorkItems = [];
        }
    }

    private void ApplyNewDefaults()
    {
        Input = new EntryFormInput
        {
            ReportDate = time.GetToday(),
            StoppedAt = "08:00",
            Remarks = [new RemarkLineInput(), new RemarkLineInput(), new RemarkLineInput()]
        };

        // Ссылка «+» со страницы проверок: ?inspectionKind=Supervising|Pdk|Pbiot.
        if (TryParseKind(InspectionKind, out var kind))
        {
            Input.Mode = EntryMode.Inspection;
            Input.Kind = kind;
        }

        if (Brigades.Count == 1)
        {
            Input.BrigadeUserId = Brigades[0].Id;
            // Для остановки «мастер по умолчанию» = название бригады; для проверок мастера выбирают явно.
            if (Input.Mode == EntryMode.Stoppage)
            {
                Input.MasterName = Brigades[0].Name;
            }
        }
    }

    private void EnsureRemarkSlots()
    {
        Input.Remarks ??= [];
        while (Input.Remarks.Count < 1)
        {
            Input.Remarks.Add(new RemarkLineInput());
        }
    }

    private void EnsureCurrentBrigadeInList(Guid brigadeUserId, string? name, string? workshop)
    {
        if (brigadeUserId == Guid.Empty || Brigades.Any(b => b.Id == brigadeUserId))
        {
            return;
        }

        Brigades.Insert(0, new BrigadeParticipant
        {
            Id = brigadeUserId,
            Name = string.IsNullOrWhiteSpace(name) ? "(бывший участник)" : name.Trim(),
            UserName = string.Empty,
            Workshop = workshop
        });
    }

    /// <summary>Выбран ли в селекте пункт «Добавить» (в модель он не биндится — это не Guid).</summary>
    private bool IsAddSelected(string field) =>
        Request.HasFormContentType
        && string.Equals(Request.Form[field].ToString(), AddValue, StringComparison.Ordinal);

    /// <summary>Сбрасывает «прилипшие» значения ModelState, чтобы asp-for показал уже обновлённую модель.</summary>
    private void ResetKeys(params string[] keys)
    {
        foreach (var key in keys)
        {
            ModelState.Remove(key);
        }
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static RemarkLineInput ToRemarkRequest(RemarkLineInput r)
    {
        var isManual = r.IsManual || r.ActItemId is null || r.ActItemId == Guid.Empty;
        return new RemarkLineInput
        {
            Id = r.Id,
            ActItemId = isManual ? null : r.ActItemId,
            Text = r.Text?.Trim() ?? string.Empty,
            Comment = r.Comment?.Trim() ?? string.Empty,
            IsManual = isManual,
            IsResolved = r.IsResolved
        };
    }

    /// <summary>
    /// Ищет активного сотрудника владельца по ФИО (без регистра) — чтобы повторное сохранение после ошибки
    /// не плодило дубли; иначе создаёт через POST api/personnel.
    /// </summary>
    private async Task<ApiResult<PersonnelPerson>> FindOrCreatePersonAsync(
        Guid ownerUserId, string fullName, string? position)
    {
        var existing = (await FetchPersonnelAsync(ownerUserId, null))
            .FirstOrDefault(p => string.Equals(p.FullName.Trim(), fullName, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return ApiResult<PersonnelPerson>.Success(existing);
        }

        var request = new SavePersonnelPersonRequest
        {
            OwnerUserId = ownerUserId,
            FullName = fullName,
            Position = position,
            IsActive = true
        };
        return await api.PostAsync<PersonnelPerson>("api/personnel", request, HttpContext.RequestAborted);
    }

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

    private async Task<List<WorkshopPersonnelItem>> FetchWorkshopPersonnelAsync(string? workshop)
    {
        if (string.IsNullOrWhiteSpace(workshop))
        {
            return [];
        }

        var url = UiHelpers.BuildUrl(
            "api/brigade-rating/workshops/personnel-by-workshop",
            ("workshop", workshop.Trim()));
        var result = await api.GetAsync<List<WorkshopPersonnelItem>>(url, HttpContext.RequestAborted);
        return result.Data ?? [];
    }

    private static bool TryParseKind(string? raw, out BrigadeInspectionKind kind) =>
        Enum.TryParse(raw?.Trim(), true, out kind) && Enum.IsDefined(kind);

    private static bool TryParseTime(string? raw, out TimeOnly value)
    {
        raw = raw?.Trim() ?? string.Empty;
        return TimeOnly.TryParseExact(raw, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
            || TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static EntryFormInput FromDetail(BrigadeStoppageDetail d) => new()
    {
        Mode = EntryMode.Stoppage,
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

    private static EntryFormInput FromInspectionDetail(BrigadeInspectionDetail d) => new()
    {
        Mode = EntryMode.Inspection,
        Kind = d.Kind,
        BrigadeUserId = d.BrigadeUserId,
        BrigadeNameHint = string.IsNullOrWhiteSpace(d.BrigadeNumber) ? null : $"Бригада №{d.BrigadeNumber}",
        ReportDate = d.ReportDate,
        MasterName = d.MasterName,
        MasterPersonId = d.MasterPersonId,
        Well = d.Well,
        DepartmentUserId = d.DepartmentUserId,
        CheckerPersonId = d.CheckerPersonId,
        CheckerName = d.CheckerName,
        CheckerPosition = d.CheckerPosition,
        FixDate = d.FixDate,
        FixDateIsManual = d.FixDateIsManual,
        FixDateManualText = d.FixDateManualText,
        Outcome = d.Outcome,
        StopPoint = d.StopPoint,
        HasExplanation = d.HasExplanation,
        FixResponsibleText = d.FixResponsibleText,
        FixReportText = d.FixReportText,
        FixResponsiblePersonId = d.FixResponsiblePersonId,
        FixResponsibleName = d.FixResponsibleName,
        Remarks = d.Remarks
            .OrderBy(r => r.SortOrder)
            .Select(r => new RemarkLineInput
            {
                Id = r.Id,
                ActItemId = r.ActItemId,
                Text = r.Text,
                Comment = r.Comment,
                IsManual = r.IsManual || r.ActItemId is null
            })
            .ToList()
    };

    /// <summary>Единая модель формы: общие поля + блоки остановки и проверки (лишнее при сохранении игнорируется).</summary>
    public sealed class EntryFormInput
    {
        public EntryMode Mode { get; set; } = EntryMode.Stoppage;
        public BrigadeInspectionKind Kind { get; set; } = BrigadeInspectionKind.Supervising;

        // ----- Общие -----
        public Guid BrigadeUserId { get; set; }
        public string? BrigadeNameHint { get; set; }
        public DateOnly ReportDate { get; set; }
        public string? MasterName { get; set; }
        public Guid? MasterPersonId { get; set; }
        public string? NewMasterPosition { get; set; }
        public string? NewMasterFullName { get; set; }
        public List<RemarkLineInput> Remarks { get; set; } = [];

        // ----- Остановка -----
        public Guid? StoppedByUserId { get; set; }
        public Guid? StoppedByPersonId { get; set; }
        public string? StoppedByName { get; set; }
        public string? NewStopperPosition { get; set; }
        public string? NewStopperFullName { get; set; }
        public string? StoppedAt { get; set; }
        public string? RestartedAt { get; set; }
        public string? WorkDescription { get; set; }
        public Guid? WorkItemId { get; set; }
        public bool IsWorkManual { get; set; } = true;

        // ----- Проверка -----
        public string? Well { get; set; }

        /// <summary>Подразделение проверяющего (владелец его персонала).</summary>
        public Guid? DepartmentUserId { get; set; }
        public Guid? CheckerPersonId { get; set; }
        public string? CheckerName { get; set; }
        public string? CheckerPosition { get; set; }
        public string? NewCheckerPosition { get; set; }
        public string? NewCheckerFullName { get; set; }
        public DateOnly? FixDate { get; set; }
        public bool FixDateIsManual { get; set; }
        public string? FixDateManualText { get; set; }
        public BrigadeInspectionOutcome? Outcome { get; set; }
        public bool StopPoint { get; set; }
        public bool HasExplanation { get; set; }
        public string? FixResponsibleText { get; set; }
        public string? FixReportText { get; set; }
        public Guid? FixResponsiblePersonId { get; set; }
        public string? FixResponsibleName { get; set; }
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
