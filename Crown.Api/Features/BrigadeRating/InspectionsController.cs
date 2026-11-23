using ClosedXML.Excel;
using Crown.Contracts.BrigadeRating;
using Crown.Contracts.Common;
using Crown.Contracts.Import;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Import;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Time;
using CatalogLookup = Crown.Infrastructure.Import.CatalogTextLookup;
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
    UserManager<ApplicationUser> users,
    AppTimeZone time,
    BrigadeInspectionImportService inspectionImport) : ControllerBase
{
    private const int MaxSequenceRetries = 3;

    // ---------- Import ----------

    [HttpPost("import/preview")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(SpreadsheetImportFileHelper.MaxFileBytes + 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportPreviewResponse>> PreviewImport(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync())
        {
            return Forbid();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.SpreadsheetImportInvalidFile));
        }

        if (file.Length > SpreadsheetImportFileHelper.MaxFileBytes)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.SpreadsheetImportFileTooLarge));
        }

        try
        {
            await using var stream = file.OpenReadStream();
            using var buffer = new MemoryStream((int)file.Length);
            await stream.CopyToAsync(buffer, cancellationToken);
            return Ok(inspectionImport.Preview(buffer.ToArray(), file.FileName));
        }
        catch (SpreadsheetImportException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.Code));
        }
    }

    [HttpPost("import/commit")]
    [RequestSizeLimit(8 * 1024 * 1024)]
    public async Task<ActionResult<SpreadsheetImportCommitResponse>> CommitImport(
        [FromBody] SpreadsheetImportCommitRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync())
        {
            return Forbid();
        }

        try
        {
            return Ok(await inspectionImport.CommitAsync(request, cancellationToken));
        }
        catch (SpreadsheetImportException ex)
        {
            return BadRequest(ApiErrors.FromCode(ex.Code));
        }
    }

    // ---------- CRUD ----------

    [HttpGet]
    public async Task<ActionResult<BrigadeInspectionsPageResponse>> List(
        [FromQuery] BrigadeInspectionKind kind,
        [FromQuery] string? period = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        if (!Enum.IsDefined(kind))
        {
            return BadRequest(new { error = "inspection_kind_invalid", message = "Укажите вид проверки." });
        }

        var (periodKey, start, end) = ResolveListPeriod(period, from, to, year, month);
        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);

        var baseQuery = db.BrigadeInspections.AsNoTracking()
            .Where(x => x.Kind == kind);

        var earliest = await baseQuery.MinAsync(x => (DateOnly?)x.ReportDate, cancellationToken);
        var earliestYear = earliest?.Year ?? time.GetToday().Year;

        var rows = await baseQuery
            .Include(x => x.Remarks)
            .Where(x => x.ReportDate >= start && x.ReportDate <= end)
            .OrderByDescending(x => x.SequenceNo)
            .ThenByDescending(x => x.Id)
            .Skip(normalizedSkip)
            .Take(normalizedTake + 1)
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > normalizedTake;
        if (hasMore)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var live = await LoadBrigadeSnapshotsAsync(rows.Select(x => x.BrigadeUserId), cancellationToken);
        var livePeople = await LoadPersonnelLiveAsync(
            rows.SelectMany(x => new Guid?[] { x.MasterPersonId, x.CheckerPersonId, x.FixResponsiblePersonId }),
            cancellationToken);
        return Ok(new BrigadeInspectionsPageResponse
        {
            Items = rows.Select(x => MapListItem(x, live, livePeople)).ToList(),
            HasMore = hasMore,
            Period = periodKey,
            PeriodStart = start,
            PeriodEnd = end,
            EarliestYear = earliestYear
        });
    }

    /// <summary>Xlsx за период. Без kind — три листа «Проверки»; с kind — один лист (в т.ч. ППК).</summary>
    [HttpGet("export.xlsx")]
    public async Task<IActionResult> ExportXlsx(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] BrigadeInspectionKind? kind = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync())
        {
            return Forbid();
        }

        if (kind is BrigadeInspectionKind bad && !Enum.IsDefined(bad))
        {
            return BadRequest(new { error = "kind_invalid" });
        }

        var (start, end) = ResolveFromTo(from, to);
        var query = db.BrigadeInspections.AsNoTracking()
            .Include(x => x.Remarks)
            .Where(x => x.ReportDate >= start && x.ReportDate <= end);
        if (kind is BrigadeInspectionKind onlyKind)
        {
            query = query.Where(x => x.Kind == onlyKind);
        }

        var rows = await query
            .OrderBy(x => x.Kind)
            .ThenBy(x => x.ReportDate)
            .ThenBy(x => x.SequenceNo)
            .ToListAsync(cancellationToken);

        var live = await LoadBrigadeSnapshotsAsync(rows.Select(x => x.BrigadeUserId), cancellationToken);
        var livePeople = await LoadPersonnelLiveAsync(
            rows.SelectMany(x => new Guid?[] { x.MasterPersonId, x.CheckerPersonId, x.FixResponsiblePersonId }),
            cancellationToken);
        var mapped = rows.Select(x => MapListItem(x, live, livePeople)).ToList();

        BrigadeInspectionKind[] kinds = kind is BrigadeInspectionKind single
            ? [single]
            :
            [
                BrigadeInspectionKind.Supervising,
                BrigadeInspectionKind.Pdk,
                BrigadeInspectionKind.Pbiot
            ];

        using var workbook = new XLWorkbook();
        foreach (var sheetKind in kinds)
        {
            var sheetName = sheetKind switch
            {
                BrigadeInspectionKind.Pdk => "ПДК",
                BrigadeInspectionKind.Pbiot => "ПБиОТ",
                BrigadeInspectionKind.Ppk => "ППК",
                _ => "Супервайзинг"
            };
            var ws = workbook.Worksheets.Add(sheetName);
            WriteExportHeader(ws, sheetKind);
            var kindRows = mapped.Where(x => x.Kind == sheetKind).ToList();
            for (var i = 0; i < kindRows.Count; i++)
            {
                WriteExportRow(ws, sheetKind, i + 2, kindRows[i]);
            }

            ws.Columns().AdjustToContents(1, 40);
        }

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var label = kind is BrigadeInspectionKind.Ppk ? "ППК" : "Проверки";
        var fileName = start == end
            ? $"{label} {start:dd.MM.yyyy}.xlsx"
            : $"{label} {start:dd.MM.yyyy}-{end:dd.MM.yyyy}.xlsx";
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
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
        if (entity is null)
        {
            return NotFound();
        }

        var live = await LoadBrigadeSnapshotsAsync([entity.BrigadeUserId], cancellationToken);
        var livePeople = await LoadPersonnelLiveAsync(
            [entity.MasterPersonId, entity.CheckerPersonId, entity.FixResponsiblePersonId],
            cancellationToken);
        return Ok(MapDetail(entity, live, livePeople));
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

        var createdLive = await LoadBrigadeSnapshotsAsync([entity.BrigadeUserId], cancellationToken);
        var createdPeople = await LoadPersonnelLiveAsync(
            [entity.MasterPersonId, entity.CheckerPersonId, entity.FixResponsiblePersonId],
            cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, MapDetail(entity, createdLive, createdPeople));
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
        var savedLive = await LoadBrigadeSnapshotsAsync([saved.BrigadeUserId], cancellationToken);
        var savedPeople = await LoadPersonnelLiveAsync(
            [saved.MasterPersonId, saved.CheckerPersonId, saved.FixResponsiblePersonId],
            cancellationToken);
        return Ok(MapDetail(saved, savedLive, savedPeople));
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
            case BrigadeInspectionKind.Ppk:
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
        entity.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
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

        // AsNoTracking-индекс — не раздуваем ChangeTracker на 6k строк.
        var index = await CatalogLookup.LoadActIndexAsync(db, cancellationToken);
        var result = new List<BrigadeInspectionRemark>();
        var order = 0;
        var catalogDirty = false;

        foreach (var r in raw)
        {
            string text;
            string? comment;
            Guid actItemId;

            if (!r.IsManual && r.ActItemId is Guid aid && aid != Guid.Empty)
            {
                if (!index.ById.TryGetValue(aid, out var lite))
                {
                    return (null, "Выбранный пункт акта не найден в справочнике.", "act_item_invalid");
                }

                if (!lite.IsActive)
                {
                    await CatalogLookup.EnsureActActiveAsync(db, lite, cancellationToken);
                    lite.IsActive = true;
                    catalogDirty = true;
                }

                text = lite.Text.Trim();
                comment = Clean(r.Comment);
                actItemId = lite.Id;
            }
            else
            {
                text = (r.Text ?? string.Empty).Trim();
                if (text.Length == 0)
                {
                    continue;
                }

                comment = Clean(r.Comment);
                var key = CatalogLookup.NormKey(text);
                if (!index.ByKey.TryGetValue(key, out var lite))
                {
                    index.MaxSortOrder++;
                    var act = new StoppageActItem
                    {
                        Id = Guid.NewGuid(),
                        Text = text,
                        IsActive = true,
                        SortOrder = index.MaxSortOrder,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    db.StoppageActItems.Add(act);
                    lite = new CatalogLookup.ActCatalogLite
                    {
                        Id = act.Id,
                        Text = act.Text,
                        IsActive = true,
                        SortOrder = act.SortOrder
                    };
                    index.ById[act.Id] = lite;
                    index.ByKey[key] = lite;
                    catalogDirty = true;
                }
                else if (!lite.IsActive)
                {
                    await CatalogLookup.EnsureActActiveAsync(db, lite, cancellationToken);
                    lite.IsActive = true;
                    catalogDirty = true;
                }

                text = lite.Text.Trim();
                actItemId = lite.Id;
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
                IsManual = false,
                SortOrder = order
            });
        }

        if (result.Count == 0)
        {
            return (null, "Добавьте хотя бы одно нарушение.", "remarks_required");
        }

        if (catalogDirty)
        {
            await db.SaveChangesAsync(cancellationToken);
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
            (r.FixResponsibleName, 200, "Ответственный за устранение"),
            (r.Notes, 2000, "Заметка")
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

    private async Task<Dictionary<Guid, (string DisplayName, string Number)>> LoadBrigadeSnapshotsAsync(
        IEnumerable<Guid> brigadeUserIds,
        CancellationToken cancellationToken)
    {
        var ids = brigadeUserIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, (string, string)>();
        }

        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.DisplayName, u.UserName, u.BrigadeNumber })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.Id,
            x => (
                UserDisplay.Format(x.DisplayName, x.UserName, x.BrigadeNumber),
                (x.BrigadeNumber ?? string.Empty).Trim()));
    }

    private static BrigadeInspectionListItemResponse MapListItem(
        BrigadeInspection x,
        IReadOnlyDictionary<Guid, (string DisplayName, string Number)>? live = null,
        IReadOnlyDictionary<Guid, PersonnelLive>? livePeople = null)
    {
        var remarks = x.Remarks.OrderBy(r => r.SortOrder).ThenBy(r => r.Text).ToList();
        var item = new BrigadeInspectionListItemResponse();
        FillCommon(item, x, live, livePeople);
        item.RemarkCount = remarks.Count;
        item.RemarkTexts = remarks.Select(FormatRemark).ToList();
        return item;
    }

    private static BrigadeInspectionDetailResponse MapDetail(
        BrigadeInspection x,
        IReadOnlyDictionary<Guid, (string DisplayName, string Number)>? live = null,
        IReadOnlyDictionary<Guid, PersonnelLive>? livePeople = null)
    {
        var remarks = x.Remarks.OrderBy(r => r.SortOrder).ThenBy(r => r.Text).ToList();
        var item = new BrigadeInspectionDetailResponse();
        FillCommon(item, x, live, livePeople);
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

    private sealed record PersonnelLive(string FullName, string? Position);

    private async Task<Dictionary<Guid, PersonnelLive>> LoadPersonnelLiveAsync(
        IEnumerable<Guid?> personIds,
        CancellationToken cancellationToken)
    {
        var ids = personIds
            .Where(x => x is Guid g && g != Guid.Empty)
            .Select(x => x!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, PersonnelLive>();
        }

        var rows = await db.PersonnelPeople.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.FullName, x.Position })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(
            x => x.Id,
            x => new PersonnelLive(x.FullName, x.Position));
    }

    private static string ResolvePersonName(
        Guid? personId,
        string snapshot,
        IReadOnlyDictionary<Guid, PersonnelLive>? livePeople)
    {
        if (personId is Guid id
            && livePeople is not null
            && livePeople.TryGetValue(id, out var live)
            && !string.IsNullOrWhiteSpace(live.FullName))
        {
            return live.FullName.Trim();
        }

        return snapshot;
    }

    private static string? ResolvePersonPosition(
        Guid? personId,
        string? snapshot,
        IReadOnlyDictionary<Guid, PersonnelLive>? livePeople)
    {
        if (personId is Guid id
            && livePeople is not null
            && livePeople.TryGetValue(id, out var live))
        {
            return string.IsNullOrWhiteSpace(live.Position) ? null : live.Position.Trim();
        }

        return snapshot;
    }

    private static void FillCommon(
        BrigadeInspectionListItemResponse t,
        BrigadeInspection x,
        IReadOnlyDictionary<Guid, (string DisplayName, string Number)>? live = null,
        IReadOnlyDictionary<Guid, PersonnelLive>? livePeople = null)
    {
        t.Id = x.Id;
        t.Kind = x.Kind;
        t.SequenceNo = x.SequenceNo;
        t.ReportDate = x.ReportDate;
        t.Workshop = x.Workshop;
        t.BrigadeUserId = x.BrigadeUserId;
        // Номер: live из пользователя, иначе снимок (старые записи).
        t.BrigadeNumber = live is not null
                          && live.TryGetValue(x.BrigadeUserId, out var snap)
                          && !string.IsNullOrWhiteSpace(snap.Number)
            ? snap.Number
            : x.BrigadeNumberSnapshot;
        t.MasterPersonId = x.MasterPersonId;
        t.MasterName = ResolvePersonName(x.MasterPersonId, x.MasterName, livePeople);
        t.Well = x.Well;
        t.CheckerPersonId = x.CheckerPersonId;
        t.CheckerName = ResolvePersonName(x.CheckerPersonId, x.CheckerName, livePeople);
        t.CheckerPosition = ResolvePersonPosition(x.CheckerPersonId, x.CheckerPosition, livePeople);
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
        t.FixResponsibleName = ResolvePersonName(
            x.FixResponsiblePersonId,
            x.FixResponsibleName ?? string.Empty,
            livePeople);
        if (string.IsNullOrWhiteSpace(t.FixResponsibleName))
        {
            t.FixResponsibleName = null;
        }

        t.Notes = x.Notes;
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

    private (string Period, DateOnly Start, DateOnly End) ResolveListPeriod(
        string? period,
        DateOnly? from,
        DateOnly? to,
        int? year,
        int? month)
    {
        var today = time.GetToday();
        var key = (period ?? string.Empty).Trim().ToLowerInvariant();

        if (key is "today" or "month" or "year")
        {
            return key switch
            {
                "today" => ("today", today, today),
                "year" => (
                    "year",
                    new DateOnly(year is >= 2000 and <= 2100 ? year.Value : today.Year, 1, 1),
                    new DateOnly(year is >= 2000 and <= 2100 ? year.Value : today.Year, 12, 31)),
                _ => ResolveMonthPreset(today, year, month)
            };
        }

        if (from is not null || to is not null)
        {
            var (start, end) = ResolveFromTo(from, to);
            return ("custom", start, end);
        }

        return ResolveMonthPreset(today, year, month);
    }

    private (DateOnly Start, DateOnly End) ResolveFromTo(DateOnly? from, DateOnly? to)
    {
        var today = time.GetToday();
        var end = to ?? today;
        var start = from ?? new DateOnly(end.Year, end.Month, 1);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return (start, end);
    }

    private static (string Period, DateOnly Start, DateOnly End) ResolveMonthPreset(
        DateOnly today,
        int? year,
        int? month)
    {
        var y = year is >= 2000 and <= 2100 ? year.Value : today.Year;
        var m = month is >= 1 and <= 12 ? month.Value : today.Month;
        var start = new DateOnly(y, m, 1);
        var end = start.AddMonths(1).AddDays(-1);
        if (y == today.Year && m == today.Month && end > today)
        {
            end = today;
        }

        return ("month", start, end);
    }

    private static void WriteExportHeader(IXLWorksheet ws, BrigadeInspectionKind kind)
    {
        string[] headers = kind switch
        {
            BrigadeInspectionKind.Pdk =>
            [
                "№", "Дата", "Цех", "Бригада №", "ФИО Мастера", "Нарушения",
                "Должность", "ФИО Проверяющего", "Дата устранения", "Ответственный за устранения"
            ],
            BrigadeInspectionKind.Pbiot =>
            [
                "№", "Дата", "Цех", "Бригада №", "ФИО Мастера", "Скважина", "Нарушения",
                "Должность", "ФИО Проверяющего", "Дата устранения", "Отчёт об устранении",
                "Ответственный за устранения"
            ],
            _ =>
            [
                "№", "Дата", "Цех", "Бригада №", "ФИО Мастера", "Предписание / Замечание / Остановка",
                "Скважина", "Нарушения", "Должность", "ФИО Проверяющего", "Остановочный пункт",
                "Объяснительная", "Дата устранения"
            ]
        };

        for (var c = 0; c < headers.Length; c++)
        {
            ws.Cell(1, c + 1).Value = headers[c];
        }

        ws.Range(1, 1, 1, headers.Length).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Length).Style.Alignment.WrapText = true;
    }

    private static void WriteExportRow(
        IXLWorksheet ws,
        BrigadeInspectionKind kind,
        int excelRow,
        BrigadeInspectionListItemResponse item)
    {
        var fixDate = item.FixDateIsManual
            ? (item.FixDateManualText ?? string.Empty)
            : (item.FixDate?.ToString("dd.MM.yyyy") ?? string.Empty);
        var remarks = string.Join("\n", item.RemarkTexts);

        ws.Cell(excelRow, 1).Value = item.SequenceNo;
        ws.Cell(excelRow, 2).Value = item.ReportDate.ToDateTime(TimeOnly.MinValue);
        ws.Cell(excelRow, 2).Style.DateFormat.Format = "dd.MM.yyyy";
        ws.Cell(excelRow, 3).Value = item.Workshop;
        ws.Cell(excelRow, 4).Value = item.BrigadeNumber;
        ws.Cell(excelRow, 5).Value = item.MasterName;

        if (kind == BrigadeInspectionKind.Pdk)
        {
            ws.Cell(excelRow, 6).Value = item.RemarkCount;
            ws.Cell(excelRow, 7).Value = item.CheckerPosition ?? string.Empty;
            ws.Cell(excelRow, 8).Value = item.CheckerName;
            ws.Cell(excelRow, 9).Value = fixDate;
            ws.Cell(excelRow, 10).Value = item.FixResponsibleText ?? string.Empty;
            return;
        }

        if (kind == BrigadeInspectionKind.Pbiot)
        {
            ws.Cell(excelRow, 6).Value = item.Well ?? string.Empty;
            ws.Cell(excelRow, 7).Value = remarks;
            ws.Cell(excelRow, 7).Style.Alignment.WrapText = true;
            ws.Cell(excelRow, 8).Value = item.CheckerPosition ?? string.Empty;
            ws.Cell(excelRow, 9).Value = item.CheckerName;
            ws.Cell(excelRow, 10).Value = fixDate;
            ws.Cell(excelRow, 11).Value = item.FixReportText ?? string.Empty;
            ws.Cell(excelRow, 12).Value = item.FixResponsibleName ?? string.Empty;
            return;
        }

        ws.Cell(excelRow, 6).Value = OutcomeLabel(item.Outcome);
        ws.Cell(excelRow, 7).Value = item.Well ?? string.Empty;
        ws.Cell(excelRow, 8).Value = remarks;
        ws.Cell(excelRow, 8).Style.Alignment.WrapText = true;
        ws.Cell(excelRow, 9).Value = item.CheckerPosition ?? string.Empty;
        ws.Cell(excelRow, 10).Value = item.CheckerName;
        ws.Cell(excelRow, 11).Value = item.StopPoint ? "Да" : "Нет";
        ws.Cell(excelRow, 12).Value = item.HasExplanation ? "Да" : "Нет";
        ws.Cell(excelRow, 13).Value = fixDate;
    }

    private static string OutcomeLabel(BrigadeInspectionOutcome? outcome) => outcome switch
    {
        BrigadeInspectionOutcome.Prescription => "Предписание",
        BrigadeInspectionOutcome.Remark => "Замечание",
        BrigadeInspectionOutcome.Stoppage => "Остановка",
        _ => string.Empty
    };

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Супервайзинг и ППК — один профиль полей.</summary>
    private static bool IsSupervisingProfile(BrigadeInspectionKind kind) =>
        kind is BrigadeInspectionKind.Supervising or BrigadeInspectionKind.Ppk;

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
