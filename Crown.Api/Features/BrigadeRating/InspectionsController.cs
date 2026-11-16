using Crown.Contracts.BrigadeRating;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Crown.Features.BrigadeRating;

/// <summary>
/// Проверки бригад (надзорные / ПДК / ПБиОТ). Права те же, что у рейтинга:
/// чтение — CanViewBrigadeRating|CanOperateBrigadeRating, запись — CanOperateBrigadeRating.
/// </summary>
[ApiController]
[Route("api/brigade-rating/inspections")]
[Authorize]
public sealed class InspectionsController(
    AppDbContext db,
    UserManager<ApplicationUser> users) : ControllerBase
{
    private const int MaxSequenceRetries = 3;

    // ---------- CRUD ----------

    [HttpGet]
    public async Task<ActionResult<List<BrigadeInspectionListItemResponse>>> List(
        [FromQuery] BrigadeInspectionKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var q = db.BrigadeInspections.AsNoTracking().Include(x => x.Remarks).AsQueryable();
        if (kind is not null)
        {
            q = q.Where(x => x.Kind == kind.Value);
        }

        var rows = await q
            .OrderBy(x => x.Kind)
            .ThenByDescending(x => x.SequenceNo)
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(MapListItem).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BrigadeInspectionDetailResponse>> Get(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var entity = await db.BrigadeInspections.AsNoTracking()
            .Include(x => x.Remarks)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return entity is null ? NotFound() : Ok(MapDetail(entity));
    }

    [HttpPost]
    public async Task<ActionResult<BrigadeInspectionDetailResponse>> Create(
        [FromBody] SaveBrigadeInspectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync())
        {
            return Forbid();
        }

        if (!Enum.IsDefined(request.Kind))
        {
            return BadRequest(new { error = "kind_invalid", message = "Не указан вид проверки." });
        }

        var built = await BuildAsync(null, request, request.Kind, cancellationToken);
        if (built.Error is not null)
        {
            return BadRequest(new { error = built.ErrorCode, message = built.Error });
        }

        var entity = built.Entity!;
        foreach (var remark in built.Remarks!)
        {
            remark.InspectionId = entity.Id;
            entity.Remarks.Add(remark);
        }

        db.BrigadeInspections.Add(entity);
        foreach (var person in built.NewPeople)
        {
            db.PersonnelPeople.Add(person);
        }

        DetachTrackedUsers();

        // Номер = max(kind)+1. Два оператора одновременно получат один и тот же номер:
        // уникальный индекс (Kind, SequenceNo) отклонит второго — пересчитываем и повторяем.
        for (var attempt = 1; ; attempt++)
        {
            var max = await db.BrigadeInspections
                .Where(x => x.Kind == entity.Kind && x.Id != entity.Id)
                .MaxAsync(x => (int?)x.SequenceNo, cancellationToken) ?? 0;
            entity.SequenceNo = max + 1;

            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateException ex) when (attempt < MaxSequenceRetries && IsUniqueViolation(ex))
            {
                // Сущности остаются Added после отката — просто пробуем с новым номером.
            }
        }

        return CreatedAtAction(nameof(Get), new { id = entity.Id }, MapDetail(entity));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BrigadeInspectionDetailResponse>> Update(
        Guid id,
        [FromBody] SaveBrigadeInspectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync())
        {
            return Forbid();
        }

        // Без Include(Remarks): иначе после ExecuteDelete трекер держит старые пункты
        // и SaveChanges даёт DbUpdateConcurrencyException (тот же приём, что в остановках).
        var existing = await db.BrigadeInspections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        // Вид и номер при правке не меняются.
        var built = await BuildAsync(existing, request, existing.Kind, cancellationToken);
        if (built.Error is not null)
        {
            return BadRequest(new { error = built.ErrorCode, message = built.Error });
        }

        // Удаление старых пунктов и запись новых — атомарно.
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.BrigadeInspectionRemarks
            .Where(x => x.InspectionId == existing.Id)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var remark in built.Remarks!)
        {
            remark.InspectionId = existing.Id;
            db.BrigadeInspectionRemarks.Add(remark);
        }

        foreach (var person in built.NewPeople)
        {
            db.PersonnelPeople.Add(person);
        }

        DetachTrackedUsers();
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var saved = await db.BrigadeInspections.AsNoTracking()
            .Include(x => x.Remarks)
            .FirstAsync(x => x.Id == id, cancellationToken);
        return Ok(MapDetail(saved));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync())
        {
            return Forbid();
        }

        var entity = await db.BrigadeInspections.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        // Пункты удаляются каскадом на стороне БД.
        db.BrigadeInspections.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---------- Персонал цеха (ПБиОТ: «ответственный за устранение») ----------

    /// <summary>Весь активный персонал бригад (роль Brigade) указанного цеха.</summary>
    [HttpGet("~/api/brigade-rating/workshops/personnel-by-workshop")]
    public async Task<ActionResult<List<WorkshopPersonnelResponse>>> PersonnelByWorkshop(
        [FromQuery] string? workshop,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        var key = (workshop ?? string.Empty).Trim();
        if (key.Length == 0)
        {
            return Ok(new List<WorkshopPersonnelResponse>());
        }

        var brigadeUsers = (await users.GetUsersInRoleAsync(AppRoles.Brigade))
            .Where(u => u.IsActive && IsSameWorkshop(u.BrigadeWorkshop, key))
            .ToDictionary(u => u.Id);
        if (brigadeUsers.Count == 0)
        {
            return Ok(new List<WorkshopPersonnelResponse>());
        }

        var ownerIds = brigadeUsers.Keys.ToList();
        var people = await db.PersonnelPeople.AsNoTracking()
            .Where(x => x.IsActive && ownerIds.Contains(x.OwnerUserId))
            .OrderBy(x => x.FullName)
            .ToListAsync(cancellationToken);

        return Ok(people.Select(p => new WorkshopPersonnelResponse
        {
            Id = p.Id,
            FullName = p.FullName,
            Position = p.Position,
            OwnerUserId = p.OwnerUserId,
            BrigadeName = UserDisplay.FormatBrigadeDisplay(brigadeUsers[p.OwnerUserId])
        }).ToList());
    }

    // ---------- Сборка сущности из запроса ----------

    private sealed class BuildResult
    {
        public BrigadeInspection? Entity { get; init; }
        public List<BrigadeInspectionRemark>? Remarks { get; init; }

        /// <summary>Новые сотрудники (мастер / проверяющий), которых нужно создать вместе с проверкой.</summary>
        public List<PersonnelPerson> NewPeople { get; init; } = [];

        public string? Error { get; init; }
        public string? ErrorCode { get; init; }

        public static BuildResult Fail(string code, string message) =>
            new() { ErrorCode = code, Error = message };
    }

    private async Task<BuildResult> BuildAsync(
        BrigadeInspection? existing,
        SaveBrigadeInspectionRequest request,
        BrigadeInspectionKind kind,
        CancellationToken cancellationToken)
    {
        if (request.ReportDate == default)
        {
            return BuildResult.Fail("report_date_required", "Укажите дату.");
        }

        var lengthError = ValidateLengths(request);
        if (lengthError is not null)
        {
            return BuildResult.Fail("field_too_long", lengthError);
        }

        // ----- Бригада: цех и номер берём из пользователя (снимок) -----
        var brigadeUser = request.BrigadeUserId == Guid.Empty
            ? null
            : await users.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == request.BrigadeUserId, cancellationToken);
        if (brigadeUser is null || !brigadeUser.IsActive)
        {
            return BuildResult.Fail("brigade_invalid", "Выберите бригаду (активный пользователь с ролью «Бригада»).");
        }

        if (!await users.IsInRoleAsync(brigadeUser, AppRoles.Brigade))
        {
            return BuildResult.Fail("brigade_not_brigade", "Проверяемая бригада должна иметь роль «Бригада».");
        }

        var newPeople = new List<PersonnelPerson>();

        // ----- Мастер -----
        Guid? masterPersonId = null;
        var masterName = string.Empty;
        var newMasterName = Clean(request.NewMasterFullName);
        if (request.MasterPersonId is Guid mpid && mpid != Guid.Empty)
        {
            var person = await db.PersonnelPeople.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == mpid && x.OwnerUserId == brigadeUser.Id, cancellationToken);
            if (person is null)
            {
                return BuildResult.Fail("master_invalid", "Выбранный мастер не найден в персонале бригады.");
            }

            // Неактивного оставляем только если он уже выбран в этой проверке.
            if (!person.IsActive && existing?.MasterPersonId != person.Id)
            {
                return BuildResult.Fail("master_inactive", "Выбранный мастер неактивен.");
            }

            masterPersonId = person.Id;
            masterName = person.FullName.Trim();
        }
        else if (newMasterName is not null)
        {
            var resolved = await ResolveOrCreatePersonAsync(
                brigadeUser.Id, newMasterName, Clean(request.NewMasterPosition), newPeople, cancellationToken);
            masterPersonId = resolved.Id;
            masterName = resolved.FullName;
        }
        else
        {
            masterName = Clean(request.MasterName) ?? string.Empty;
        }

        if (kind == BrigadeInspectionKind.Supervising && masterName.Length == 0)
        {
            return BuildResult.Fail("master_required", "Укажите мастера.");
        }

        // ----- Подразделение проверяющего -----
        Guid? departmentUserId = null;
        string? departmentName = null;
        if (request.DepartmentUserId is Guid did && did != Guid.Empty)
        {
            var dept = await users.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == did, cancellationToken);
            if (dept is null || !dept.IsActive)
            {
                return BuildResult.Fail("department_invalid", "Подразделение проверяющего не найдено.");
            }

            if (!await users.IsInRoleAsync(dept, AppRoles.Department))
            {
                return BuildResult.Fail("department_not_department", "Проверяющий — только от подразделения.");
            }

            departmentUserId = dept.Id;
            departmentName = UserDisplay.FormatUserDisplay(dept, isBrigade: false);
        }

        // ----- Проверяющий -----
        Guid? checkerPersonId = null;
        string checkerName;
        string? checkerPosition;
        var newCheckerName = Clean(request.NewCheckerFullName);
        if (request.CheckerPersonId is Guid cpid && cpid != Guid.Empty)
        {
            var person = await db.PersonnelPeople.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == cpid, cancellationToken);
            if (person is null || (departmentUserId is not null && person.OwnerUserId != departmentUserId))
            {
                return BuildResult.Fail("checker_invalid", "Проверяющий не найден в персонале выбранного подразделения.");
            }

            if (!person.IsActive && existing?.CheckerPersonId != person.Id)
            {
                return BuildResult.Fail("checker_inactive", "Выбранный проверяющий неактивен.");
            }

            checkerPersonId = person.Id;
            checkerName = person.FullName.Trim();
            checkerPosition = Clean(person.Position);
        }
        else if (newCheckerName is not null)
        {
            // Нового проверяющего некуда записать без подразделения-владельца персонала.
            if (departmentUserId is null)
            {
                return BuildResult.Fail("checker_department_required", "Сначала выберите подразделение проверяющего.");
            }

            var resolved = await ResolveOrCreatePersonAsync(
                departmentUserId.Value, newCheckerName, Clean(request.NewCheckerPosition), newPeople, cancellationToken);
            checkerPersonId = resolved.Id;
            checkerName = resolved.FullName;
            checkerPosition = Clean(resolved.Position);
        }
        else
        {
            checkerName = Clean(request.CheckerName) ?? string.Empty;
            checkerPosition = Clean(request.CheckerPosition);
        }

        if (checkerName.Length == 0)
        {
            return BuildResult.Fail("checker_required", "Укажите проверяющего.");
        }

        // ----- Дата устранения: либо дата, либо произвольный текст -----
        DateOnly? fixDate = null;
        string? fixDateText = null;
        if (request.FixDateIsManual)
        {
            fixDateText = Clean(request.FixDateManualText);
        }
        else
        {
            fixDate = request.FixDate;
        }

        // ----- Поля, зависящие от вида: чужие обнуляем, чтобы не копить мусор -----
        BrigadeInspectionOutcome? outcome = null;
        var stopPoint = false;
        var hasExplanation = false;
        string? fixResponsibleText = null;
        string? fixReportText = null;
        Guid? fixResponsiblePersonId = null;
        string? fixResponsibleName = null;
        string? well = null;

        switch (kind)
        {
            case BrigadeInspectionKind.Supervising:
                if (request.Outcome is not BrigadeInspectionOutcome o || !Enum.IsDefined(o))
                {
                    return BuildResult.Fail("outcome_required", "Укажите: предписание, замечание или остановка.");
                }

                outcome = o;
                stopPoint = request.StopPoint;
                hasExplanation = request.HasExplanation;
                well = Clean(request.Well);
                break;

            case BrigadeInspectionKind.Pdk:
                fixResponsibleText = Clean(request.FixResponsibleText);
                break;

            case BrigadeInspectionKind.Pbiot:
                well = Clean(request.Well);
                fixReportText = Clean(request.FixReportText);
                if (request.FixResponsiblePersonId is Guid frid && frid != Guid.Empty)
                {
                    var resp = await ResolveWorkshopResponsibleAsync(frid, brigadeUser, existing, cancellationToken);
                    if (resp.Error is not null)
                    {
                        return BuildResult.Fail(resp.ErrorCode!, resp.Error);
                    }

                    fixResponsiblePersonId = resp.Person!.Id;
                    fixResponsibleName = resp.Person.FullName.Trim();
                }
                else
                {
                    fixResponsibleName = Clean(request.FixResponsibleName);
                }

                break;
        }

        // ----- Нарушения -----
        var built = await BuildRemarksAsync(request.Remarks, cancellationToken);
        if (built.Error is not null)
        {
            return BuildResult.Fail(built.ErrorCode!, built.Error);
        }

        // ----- Запись в сущность -----
        var entity = existing ?? new BrigadeInspection
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            CreatedAt = DateTimeOffset.UtcNow
        };

        // Цех/номер переснимаем только при смене бригады (или пустом снимке), иначе история не «плывёт».
        if (existing is null
            || existing.BrigadeUserId != brigadeUser.Id
            || string.IsNullOrEmpty(existing.Workshop))
        {
            entity.Workshop = Clean(brigadeUser.BrigadeWorkshop) ?? string.Empty;
            entity.BrigadeNumberSnapshot = Clean(brigadeUser.BrigadeNumber) ?? string.Empty;
        }

        entity.BrigadeUserId = brigadeUser.Id;
        entity.ReportDate = request.ReportDate;
        entity.MasterPersonId = masterPersonId;
        entity.MasterName = masterName;
        entity.Well = well;
        entity.CheckerPersonId = checkerPersonId;
        entity.CheckerName = checkerName;
        entity.CheckerPosition = checkerPosition;
        entity.DepartmentUserId = departmentUserId;
        entity.DepartmentName = departmentName;
        entity.FixDate = fixDate;
        entity.FixDateIsManual = request.FixDateIsManual;
        entity.FixDateManualText = fixDateText;
        entity.Outcome = outcome;
        entity.StopPoint = stopPoint;
        entity.HasExplanation = hasExplanation;
        entity.FixResponsibleText = fixResponsibleText;
        entity.FixReportText = fixReportText;
        entity.FixResponsiblePersonId = fixResponsiblePersonId;
        entity.FixResponsibleName = fixResponsibleName;
        if (existing is not null)
        {
            entity.UpdatedAt = DateTimeOffset.UtcNow;
        }

        return new BuildResult { Entity = entity, Remarks = built.Items, NewPeople = newPeople };
    }

    /// <summary>
    /// Ищет сотрудника владельца по ФИО (без учёта регистра) — чтобы повторный ввод не плодил дубли;
    /// иначе создаёт нового и кладёт в <paramref name="newPeople"/> (в БД попадёт вместе с проверкой).
    /// </summary>
    private async Task<PersonnelPerson> ResolveOrCreatePersonAsync(
        Guid ownerUserId,
        string fullName,
        string? position,
        List<PersonnelPerson> newPeople,
        CancellationToken cancellationToken)
    {
        var lower = fullName.ToLower();
        var found = await db.PersonnelPeople.AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OwnerUserId == ownerUserId && x.IsActive && x.FullName.ToLower() == lower,
                cancellationToken);
        if (found is not null)
        {
            return found;
        }

        var person = new PersonnelPerson
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            FullName = fullName,
            Position = position,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        newPeople.Add(person);
        return person;
    }

    /// <summary>Ответственный за устранение (ПБиОТ): сотрудник бригады той же роли Brigade и того же цеха.</summary>
    private async Task<(PersonnelPerson? Person, string? Error, string? ErrorCode)> ResolveWorkshopResponsibleAsync(
        Guid personId,
        ApplicationUser brigadeUser,
        BrigadeInspection? existing,
        CancellationToken cancellationToken)
    {
        var person = await db.PersonnelPeople.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == personId, cancellationToken);
        if (person is null)
        {
            return (null, "Ответственный за устранение не найден.", "fix_responsible_invalid");
        }

        if (!person.IsActive && existing?.FixResponsiblePersonId != person.Id)
        {
            return (null, "Выбранный ответственный неактивен.", "fix_responsible_inactive");
        }

        var owner = await users.Users.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == person.OwnerUserId, cancellationToken);
        if (owner is null
            || !await users.IsInRoleAsync(owner, AppRoles.Brigade)
            || !IsSameWorkshop(owner.BrigadeWorkshop, brigadeUser.BrigadeWorkshop))
        {
            return (null, "Ответственный должен быть из бригады того же цеха.", "fix_responsible_workshop_mismatch");
        }

        return (person, null, null);
    }

    private async Task<(List<BrigadeInspectionRemark>? Items, string? Error, string? ErrorCode)> BuildRemarksAsync(
        List<SaveInspectionRemarkRequest>? requestRemarks,
        CancellationToken cancellationToken)
    {
        var raw = requestRemarks ?? [];
        if (raw.Count == 0)
        {
            return (null, "Добавьте хотя бы одно нарушение.", "remarks_required");
        }

        var actIds = raw
            .Where(r => !r.IsManual && r.ActItemId is Guid)
            .Select(r => r.ActItemId!.Value)
            .Distinct()
            .ToList();
        var actMap = actIds.Count == 0
            ? new Dictionary<Guid, StoppageActItem>()
            : await db.StoppageActItems.AsNoTracking()
                .Where(x => actIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);

        var result = new List<BrigadeInspectionRemark>();
        var order = 0;
        foreach (var r in raw)
        {
            var isManual = r.IsManual || r.ActItemId is null || r.ActItemId == Guid.Empty;
            string text;
            string? comment;
            Guid? actItemId;

            if (isManual)
            {
                text = (r.Text ?? string.Empty).Trim();
                comment = null;
                actItemId = null;
                if (text.Length == 0)
                {
                    continue; // пустую ручную строку молча пропускаем
                }
            }
            else
            {
                if (!actMap.TryGetValue(r.ActItemId!.Value, out var act))
                {
                    return (null, "Выбранный пункт акта не найден в справочнике.", "act_item_invalid");
                }

                text = act.Text.Trim();
                comment = Clean(r.Comment);
                actItemId = act.Id;
            }

            if (text.Length > 2000 || (comment?.Length ?? 0) > 1000)
            {
                return (null, "Текст нарушения (до 2000) или комментарий (до 1000) слишком длинный.", "remark_too_long");
            }

            order++;
            result.Add(new BrigadeInspectionRemark
            {
                Id = Guid.NewGuid(),
                ActItemId = actItemId,
                Text = text,
                Comment = comment,
                IsManual = isManual,
                SortOrder = order
            });
        }

        if (result.Count == 0)
        {
            return (null, "Добавьте хотя бы одно нарушение.", "remarks_required");
        }

        return (result, null, null);
    }

    /// <summary>Колонки varchar(N) — ловим длинные значения до БД, чтобы вернуть 400, а не 500.</summary>
    private static string? ValidateLengths(SaveBrigadeInspectionRequest r)
    {
        (string? Value, int Max, string Label)[] fields =
        [
            (r.MasterName, 200, "ФИО мастера"),
            (r.NewMasterFullName, 200, "ФИО нового мастера"),
            (r.NewMasterPosition, 200, "Должность нового мастера"),
            (r.Well, 200, "Скважина"),
            (r.CheckerName, 200, "ФИО проверяющего"),
            (r.CheckerPosition, 200, "Должность проверяющего"),
            (r.NewCheckerFullName, 200, "ФИО нового проверяющего"),
            (r.NewCheckerPosition, 200, "Должность нового проверяющего"),
            (r.FixDateManualText, 200, "Дата устранения (текст)"),
            (r.FixResponsibleText, 500, "Ответственный за устранение"),
            (r.FixReportText, 2000, "Отчёт об устранении"),
            (r.FixResponsibleName, 200, "Ответственный за устранение")
        ];

        foreach (var (value, max, label) in fields)
        {
            if (value is not null && value.Trim().Length > max)
            {
                return $"Поле «{label}» длиннее {max} символов.";
            }
        }

        return null;
    }

    // ---------- Маппинг ----------

    private static BrigadeInspectionListItemResponse MapListItem(BrigadeInspection x)
    {
        var remarks = x.Remarks.OrderBy(r => r.SortOrder).ThenBy(r => r.Text).ToList();
        var item = new BrigadeInspectionListItemResponse();
        FillCommon(item, x);
        item.RemarkCount = remarks.Count;
        item.RemarkTexts = remarks.Select(FormatRemark).ToList();
        return item;
    }

    private static BrigadeInspectionDetailResponse MapDetail(BrigadeInspection x)
    {
        var remarks = x.Remarks.OrderBy(r => r.SortOrder).ThenBy(r => r.Text).ToList();
        var item = new BrigadeInspectionDetailResponse();
        FillCommon(item, x);
        item.RemarkCount = remarks.Count;
        item.RemarkTexts = remarks.Select(FormatRemark).ToList();
        item.Remarks = remarks.Select(r => new BrigadeInspectionRemarkResponse
        {
            Id = r.Id,
            ActItemId = r.ActItemId,
            Text = r.Text,
            Comment = r.Comment,
            IsManual = r.IsManual,
            SortOrder = r.SortOrder
        }).ToList();
        return item;
    }

    private static void FillCommon(BrigadeInspectionListItemResponse t, BrigadeInspection x)
    {
        t.Id = x.Id;
        t.Kind = x.Kind;
        t.SequenceNo = x.SequenceNo;
        t.ReportDate = x.ReportDate;
        t.Workshop = x.Workshop;
        t.BrigadeUserId = x.BrigadeUserId;
        t.BrigadeNumber = x.BrigadeNumberSnapshot;
        t.MasterPersonId = x.MasterPersonId;
        t.MasterName = x.MasterName;
        t.Well = x.Well;
        t.CheckerPersonId = x.CheckerPersonId;
        t.CheckerName = x.CheckerName;
        t.CheckerPosition = x.CheckerPosition;
        t.FixDate = x.FixDate;
        t.FixDateIsManual = x.FixDateIsManual;
        t.FixDateManualText = x.FixDateManualText;
        t.Outcome = x.Outcome;
        t.StopPoint = x.StopPoint;
        t.HasExplanation = x.HasExplanation;
        t.DepartmentUserId = x.DepartmentUserId;
        t.DepartmentName = x.DepartmentName;
        t.FixResponsibleText = x.FixResponsibleText;
        t.FixReportText = x.FixReportText;
        t.FixResponsiblePersonId = x.FixResponsiblePersonId;
        t.FixResponsibleName = x.FixResponsibleName;
        t.CreatedAt = x.CreatedAt;
        t.UpdatedAt = x.UpdatedAt;
    }

    private static string FormatRemark(BrigadeInspectionRemark r)
    {
        var text = r.Text.Trim();
        return !r.IsManual && !string.IsNullOrWhiteSpace(r.Comment)
            ? $"{text} ({r.Comment.Trim()})"
            : text;
    }

    // ---------- Helpers ----------

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsSameWorkshop(string? a, string? b)
    {
        var x = Clean(a);
        var y = Clean(b);
        return x is not null && y is not null && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>UserManager шарит DbContext — иначе SaveChanges может UPDATE AspNetUsers по stale ConcurrencyStamp.</summary>
    private void DetachTrackedUsers()
    {
        foreach (var entry in db.ChangeTracker.Entries<ApplicationUser>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    // Права — копия логики BrigadeRatingController (staff / матрица ролей Department|Customer|Brigade).

    private async Task<bool> CanViewAsync()
    {
        if (IsStaff())
        {
            return true;
        }

        if (!IsMatrixRole())
        {
            return false;
        }

        var user = await users.GetUserAsync(User);
        return user is { IsActive: true }
            && (user.CanViewBrigadeRating || user.CanOperateBrigadeRating);
    }

    private async Task<bool> CanOperateAsync()
    {
        if (IsStaff())
        {
            return true;
        }

        if (!IsMatrixRole())
        {
            return false;
        }

        var user = await users.GetUserAsync(User);
        return user is { IsActive: true, CanOperateBrigadeRating: true };
    }

    private bool IsMatrixRole() =>
        AppRoles.SwitchableRoles.Any(User.IsInRole);

    private bool IsStaff() =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);
}
