using System.Security.Claims;
using ClosedXML.Excel;
using Crown.Contracts.BrigadeRating;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crown.Features.BrigadeRating;

[ApiController]
[Route("api/brigade-rating")]
[Authorize]
public sealed class BrigadeRatingController(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    AppTimeZone time) : ControllerBase
{
    // ---------- Участники (Department + CanViewBrigadeRating) ----------

    [HttpGet("brigades")]
    public async Task<ActionResult<List<BrigadeParticipantResponse>>> ListParticipants(
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        var departmentUsers = await users.GetUsersInRoleAsync(AppRoles.Department);
        IEnumerable<ApplicationUser> q = departmentUsers
            .Where(u => u.IsActive && u.CanViewBrigadeRating);

        // Staff и оператор видят всех участников; обычная бригада — только себя.
        if (!IsStaff() && !await CanOperateAsync(cancellationToken))
        {
            var uid = CurrentUserId();
            q = q.Where(u => u.Id == uid);
        }

        var items = q
            .OrderBy(u => DisplayOf(u))
            .Select(u => new BrigadeParticipantResponse
            {
                Id = u.Id,
                Name = DisplayOf(u),
                UserName = u.UserName ?? string.Empty
            })
            .ToList();

        return Ok(items);
    }

    // ---------- Справочник пунктов акта ----------

    [HttpGet("act-items")]
    public async Task<ActionResult<List<StoppageActItemResponse>>> ListActItems(
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        var q = db.StoppageActItems.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            q = q.Where(x => x.IsActive);
        }

        var items = await q
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Text)
            .Select(x => new StoppageActItemResponse
            {
                Id = x.Id,
                Text = x.Text,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("act-items")]
    public async Task<ActionResult<StoppageActItemResponse>> CreateActItem(
        [FromBody] SaveStoppageActItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        var text = (request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(new { error = "act_item_text_required", message = "Укажите текст пункта." });
        }

        var maxOrder = await db.StoppageActItems.MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? 0;
        var entity = new StoppageActItem
        {
            Id = Guid.NewGuid(),
            Text = text,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder > 0 ? request.SortOrder : maxOrder + 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StoppageActItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListActItems), MapActItem(entity));
    }

    [HttpPut("act-items/{id:guid}")]
    public async Task<ActionResult<StoppageActItemResponse>> UpdateActItem(
        Guid id,
        [FromBody] SaveStoppageActItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        var entity = await db.StoppageActItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var text = (request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(new { error = "act_item_text_required", message = "Укажите текст пункта." });
        }

        entity.Text = text;
        entity.IsActive = request.IsActive;
        if (request.SortOrder > 0)
        {
            entity.SortOrder = request.SortOrder;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(MapActItem(entity));
    }

    [HttpDelete("act-items/{id:guid}")]
    public async Task<IActionResult> DeleteActItem(Guid id, CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        var entity = await db.StoppageActItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        // В остановках: пункт → «Вручную», текст и комментарий сохраняем.
        var linked = await db.StoppageRemarks
            .Where(x => x.ActItemId == id)
            .ToListAsync(cancellationToken);
        foreach (var r in linked)
        {
            r.ActItemId = null;
            r.IsManual = true;
            // Text / Comment не трогаем.
        }

        db.StoppageActItems.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---------- Справочник выполняемых работ ----------

    [HttpGet("work-items")]
    public async Task<ActionResult<List<StoppageWorkItemResponse>>> ListWorkItems(
        [FromQuery] bool activeOnly = false,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        var q = db.StoppageWorkItems.AsNoTracking().AsQueryable();
        if (activeOnly)
        {
            q = q.Where(x => x.IsActive);
        }

        var items = await q
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Text)
            .Select(x => new StoppageWorkItemResponse
            {
                Id = x.Id,
                Text = x.Text,
                IsActive = x.IsActive,
                SortOrder = x.SortOrder
            })
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPost("work-items")]
    public async Task<ActionResult<StoppageWorkItemResponse>> CreateWorkItem(
        [FromBody] SaveStoppageWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        var text = (request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(new { error = "work_item_text_required", message = "Укажите текст работы." });
        }

        var maxOrder = await db.StoppageWorkItems.MaxAsync(x => (int?)x.SortOrder, cancellationToken) ?? 0;
        var entity = new StoppageWorkItem
        {
            Id = Guid.NewGuid(),
            Text = text,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder > 0 ? request.SortOrder : maxOrder + 1,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.StoppageWorkItems.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(ListWorkItems), MapWorkItem(entity));
    }

    [HttpPut("work-items/{id:guid}")]
    public async Task<ActionResult<StoppageWorkItemResponse>> UpdateWorkItem(
        Guid id,
        [FromBody] SaveStoppageWorkItemRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        var entity = await db.StoppageWorkItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var text = (request.Text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return BadRequest(new { error = "work_item_text_required", message = "Укажите текст работы." });
        }

        entity.Text = text;
        entity.IsActive = request.IsActive;
        if (request.SortOrder > 0)
        {
            entity.SortOrder = request.SortOrder;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Ok(MapWorkItem(entity));
    }

    [HttpDelete("work-items/{id:guid}")]
    public async Task<IActionResult> DeleteWorkItem(Guid id, CancellationToken cancellationToken)
    {
        if (!IsStaff())
        {
            return Forbid();
        }

        var entity = await db.StoppageWorkItems.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        // В остановках: работа → «Вручную», снимок WorkDescription сохраняем.
        var linked = await db.BrigadeStoppages
            .Where(x => x.WorkItemId == id)
            .ToListAsync(cancellationToken);
        foreach (var s in linked)
        {
            s.WorkItemId = null;
            s.IsWorkManual = true;
            // WorkDescription не трогаем.
        }

        db.StoppageWorkItems.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---------- Stoppages ----------

    [HttpGet("stoppages")]
    public async Task<ActionResult<List<BrigadeStoppageListItemResponse>>> ListStoppages(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        var (start, end) = ResolvePeriod(from, to);
        // Полный рейтинг / список: любой с доступом к разделу видит все бригады.
        var seeAll = true;
        var rows = await ScopeStoppages(db.BrigadeStoppages.AsNoTracking()
                .Include(x => x.Remarks)
                .Where(x => x.ReportDate >= start && x.ReportDate <= end), seeAll)
            .OrderByDescending(x => x.ReportDate)
            .ThenByDescending(x => x.StoppedAt)
            .ToListAsync(cancellationToken);

        return Ok(rows.Select(MapListItem).ToList());
    }

    [HttpGet("stoppages/{id:guid}")]
    public async Task<ActionResult<BrigadeStoppageDetailResponse>> GetStoppage(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        // Полный рейтинг / список: любой с доступом к разделу видит все бригады.
        var seeAll = true;
        var entity = await ScopeStoppages(db.BrigadeStoppages.AsNoTracking()
                .Include(x => x.Remarks), seeAll)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        return Ok(MapDetail(entity));
    }

    [HttpPost("stoppages")]
    public async Task<ActionResult<BrigadeStoppageDetailResponse>> CreateStoppage(
        [FromBody] SaveBrigadeStoppageRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync(cancellationToken))
        {
            return Forbid();
        }

        var built = await BuildStoppageAsync(null, request, cancellationToken);
        if (built.Error is not null)
        {
            return BadRequest(new { error = built.ErrorCode, message = built.Error });
        }

        var entity = built.Entity!;
        if (built.NewRemarks is { Count: > 0 })
        {
            foreach (var remark in built.NewRemarks)
            {
                remark.BrigadeStoppageId = entity.Id;
                entity.Remarks.Add(remark);
            }
        }

        db.BrigadeStoppages.Add(entity);
        DetachTrackedUsers();
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(GetStoppage), new { id = entity.Id }, MapDetail(entity));
    }

    [HttpPut("stoppages/{id:guid}")]
    public async Task<ActionResult<BrigadeStoppageDetailResponse>> UpdateStoppage(
        Guid id,
        [FromBody] SaveBrigadeStoppageRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync(cancellationToken))
        {
            return Forbid();
        }

        // Без Include(Remarks): иначе после ExecuteDelete трекер ещё держит старые пункты и SaveChanges
        // шлёт DELETE/UPDATE по уже удалённым → DbUpdateConcurrencyException.
        var existing = await db.BrigadeStoppages
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var built = await BuildStoppageAsync(existing, request, cancellationToken);
        if (built.Error is not null)
        {
            return BadRequest(new { error = built.ErrorCode, message = built.Error });
        }

        await db.StoppageRemarks
            .Where(x => x.BrigadeStoppageId == existing.Id)
            .ExecuteDeleteAsync(cancellationToken);

        foreach (var remark in built.NewRemarks!)
        {
            remark.BrigadeStoppageId = existing.Id;
            db.StoppageRemarks.Add(remark);
        }

        DetachTrackedUsers();
        await db.SaveChangesAsync(cancellationToken);

        var saved = await db.BrigadeStoppages.AsNoTracking()
            .Include(x => x.Remarks)
            .FirstAsync(x => x.Id == id, cancellationToken);
        return Ok(MapDetail(saved));
    }

    [HttpPatch("remarks/{id:guid}/resolved")]
    public async Task<ActionResult<StoppageRemarkResponse>> SetRemarkResolved(
        Guid id,
        [FromBody] SetRemarkResolvedRequest request,
        CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        var remark = await db.StoppageRemarks
            .Include(x => x.Stoppage)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (remark is null)
        {
            return NotFound();
        }

        if (!IsStaff() && !await CanOperateAsync(cancellationToken))
        {
            var uid = CurrentUserId();
            if (remark.Stoppage.BrigadeUserId != uid)
            {
                return Forbid();
            }
        }

        remark.IsResolved = request.IsResolved;
        remark.ResolvedAt = request.IsResolved ? DateTimeOffset.UtcNow : null;
        await db.SaveChangesAsync(cancellationToken);

        return Ok(MapRemark(remark));
    }

    [HttpDelete("stoppages/{id:guid}")]
    public async Task<IActionResult> DeleteStoppage(Guid id, CancellationToken cancellationToken)
    {
        if (!await CanOperateAsync(cancellationToken))
        {
            return Forbid();
        }

        var entity = await db.BrigadeStoppages.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        db.BrigadeStoppages.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    // ---------- Dashboard + export ----------

    [HttpGet("dashboard")]
    public async Task<ActionResult<BrigadeRatingDashboardResponse>> Dashboard(
        [FromQuery] string? period = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] int? year = null,
        [FromQuery] int? month = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        var (periodKey, start, end) = ResolveDashboardPeriod(period, from, to, year, month);
        var (prevStart, prevEnd) = PreviousPeriodBounds(periodKey, start, end);
        // Полный рейтинг / список: любой с доступом к разделу видит все бригады.
        var seeAll = true;

        var earliestDate = await ScopeStoppages(db.BrigadeStoppages.AsNoTracking(), seeAll)
            .Select(x => (DateOnly?)x.ReportDate)
            .MinAsync(cancellationToken);
        var earliestYear = earliestDate?.Year ?? time.GetToday().Year;

        var rows = await ScopeStoppages(db.BrigadeStoppages.AsNoTracking()
                .Include(x => x.Remarks)
                .Where(x => x.ReportDate >= start && x.ReportDate <= end), seeAll)
            .ToListAsync(cancellationToken);

        var prevRows = await ScopeStoppages(db.BrigadeStoppages.AsNoTracking()
                .Include(x => x.Remarks)
                .Where(x => x.ReportDate >= prevStart && x.ReportDate <= prevEnd), seeAll)
            .ToListAsync(cancellationToken);

        var remarkFlat = rows.SelectMany(x => x.Remarks).ToList();
        var prevRemarks = prevRows.SelectMany(x => x.Remarks).ToList();
        var resolved = remarkFlat.Count(x => x.IsResolved);
        var unresolved = remarkFlat.Count(x => !x.IsResolved);
        var prevResolved = prevRemarks.Count(x => x.IsResolved);
        var prevUnresolved = prevRemarks.Count(x => !x.IsResolved);
        var totalHours = rows.Sum(x => x.Hours);
        var prevHours = prevRows.Sum(x => x.Hours);

        var ranking = rows
            .GroupBy(x => x.BrigadeUserId)
            .Select(g =>
            {
                var first = g.First();
                var remarks = g.SelectMany(x => x.Remarks).ToList();
                var open = remarks.Count(r => !r.IsResolved);
                var hours = g.Sum(x => x.Hours);
                var stoppages = g.Count();
                var score = open * 10m + hours + stoppages;
                return new BrigadeRankItemResponse
                {
                    BrigadeUserId = g.Key,
                    BrigadeName = first.BrigadeName,
                    MasterName = first.MasterName,
                    StoppageCount = stoppages,
                    RemarkCount = remarks.Count,
                    UnresolvedRemarkCount = open,
                    TotalHours = hours,
                    Score = score
                };
            })
            .OrderBy(x => x.Score)
            .ThenBy(x => x.BrigadeName)
            .ToList();

        // Топ по тексту пункта (без комментария) — справочник даёт повторяемость.
        var top = remarkFlat
            .GroupBy(x => x.Text.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new RemarkTopItemResponse
            {
                Text = g.First().Text.Trim(),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Text)
            .Take(10)
            .ToList();

        // Топ по тексту работы; пустые (не указана) пропускаем.
        var topWorks = rows
            .Where(x => !string.IsNullOrWhiteSpace(x.WorkDescription))
            .GroupBy(x => x.WorkDescription.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new WorkTopItemResponse
            {
                Text = g.First().WorkDescription.Trim(),
                Count = g.Count()
            })
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Text)
            .Take(10)
            .ToList();

        var topStoppers = rows
            .GroupBy(x => string.IsNullOrWhiteSpace(x.StoppedByName) ? "—" : x.StoppedByName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => new StopperTopItemResponse
            {
                Name = g.First().StoppedByName.Trim().Length > 0 ? g.First().StoppedByName.Trim() : "—",
                StoppageCount = g.Count(),
                TotalHours = g.Sum(x => x.Hours)
            })
            .OrderByDescending(x => x.StoppageCount)
            .ThenByDescending(x => x.TotalHours)
            .ThenBy(x => x.Name)
            .Take(10)
            .ToList();

        return Ok(new BrigadeRatingDashboardResponse
        {
            Period = periodKey,
            EarliestYear = earliestYear,
            PeriodStart = start,
            PeriodEnd = end,
            StoppageCount = rows.Count,
            RemarkCount = remarkFlat.Count,
            ResolvedRemarkCount = resolved,
            UnresolvedRemarkCount = unresolved,
            TotalHours = totalHours,
            ResolutionRatePercent = Percent(resolved, remarkFlat.Count),
            AverageHours = AverageHours(rows),
            PeriodComparison = new BrigadeRatingPeriodComparisonResponse
            {
                PreviousPeriodStart = prevStart,
                PreviousPeriodEnd = prevEnd,
                PreviousStoppageCount = prevRows.Count,
                PreviousRemarkCount = prevRemarks.Count,
                PreviousResolvedRemarkCount = prevResolved,
                PreviousUnresolvedRemarkCount = prevUnresolved,
                PreviousTotalHours = prevHours,
                PreviousResolutionRatePercent = Percent(prevResolved, prevRemarks.Count),
                PreviousAverageHours = AverageHours(prevRows)
            },
            DailyTrend = BuildDailyTrend(periodKey, start, end, rows),
            TopStoppers = topStoppers,
            BrigadeRanking = ranking,
            TopRemarks = top,
            TopWorks = topWorks,
            Stoppages = rows
                .OrderByDescending(x => x.ReportDate)
                .ThenByDescending(x => x.StoppedAt)
                .Select(MapListItem)
                .ToList()
        });
    }

    [HttpGet("export.xlsx")]
    public async Task<IActionResult> ExportXlsx(
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        CancellationToken cancellationToken = default)
    {
        if (!await CanViewAsync(cancellationToken))
        {
            return Forbid();
        }

        var (start, end) = ResolvePeriod(from, to);
        // Полный рейтинг / список: любой с доступом к разделу видит все бригады.
        var seeAll = true;
        var rows = await ScopeStoppages(db.BrigadeStoppages.AsNoTracking()
                .Include(x => x.Remarks)
                .Where(x => x.ReportDate >= start && x.ReportDate <= end), seeAll)
            .OrderBy(x => x.ReportDate)
            .ThenBy(x => x.StoppedAt)
            .ToListAsync(cancellationToken);

        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Анализ остановок");
        ws.Cell(1, 1).Value = "№ п/п";
        ws.Cell(1, 2).Value = "Дата\n(отчетные сутки)";
        ws.Cell(1, 3).Value = "Бригада";
        ws.Cell(1, 4).Value = "ФИО\nМастера";
        ws.Cell(1, 5).Value = "Должность\nФИО\n(кто остановил бригаду)";
        ws.Cell(1, 6).Value = "Время остановки";
        ws.Cell(1, 7).Value = "Время запуска";
        ws.Cell(1, 8).Value = "Общее количество часов";
        ws.Cell(1, 9).Value = "Количество пунктов";
        ws.Cell(1, 10).Value = "Выполняемая работа";
        ws.Cell(1, 11).Value = "Пункты по Акту предписания\n(Из за чего остановлена бригада)";
        ws.Range(1, 1, 1, 11).Style.Font.Bold = true;
        ws.Range(1, 1, 1, 11).Style.Alignment.WrapText = true;

        var n = 1;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var excelRow = i + 2;
            var remarks = row.Remarks.OrderBy(x => x.SortOrder).ThenBy(x => x.Text).ToList();
            // Каждый пункт с новой строки; комментарий справочника — в скобках.
            var remarkText = string.Join(
                "\n",
                remarks.Select((r, idx) => FormatRemarkForExcel(idx + 1, r)));

            ws.Cell(excelRow, 1).Value = n++;
            ws.Cell(excelRow, 2).Value = row.ReportDate.ToDateTime(TimeOnly.MinValue);
            ws.Cell(excelRow, 2).Style.DateFormat.Format = "dd.MM.yyyy";
            ws.Cell(excelRow, 3).Value = row.BrigadeName;
            ws.Cell(excelRow, 4).Value = row.MasterName;
            ws.Cell(excelRow, 5).Value = row.StoppedByName;
            ws.Cell(excelRow, 6).Value = row.StoppedAt.ToString("HH:mm");
            ws.Cell(excelRow, 7).Value = row.RestartedAt?.ToString("HH:mm") ?? string.Empty;
            ws.Cell(excelRow, 8).Value = row.Hours;
            ws.Cell(excelRow, 9).Value = remarks.Count;
            ws.Cell(excelRow, 10).Value = row.WorkDescription;
            ws.Cell(excelRow, 11).Value = remarkText;
            ws.Cell(excelRow, 11).Style.Alignment.WrapText = true;
            ws.Cell(excelRow, 11).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }

        ws.Columns().AdjustToContents(1, 40);
        ws.Column(11).Width = 60;

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var bytes = stream.ToArray();
        var fileName = start == end
            ? $"Анализ Остановок {start:dd.MM.yyyy}.xlsx"
            : $"Анализ Остановок {start:dd.MM.yyyy}-{end:dd.MM.yyyy}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // ---------- Helpers ----------

    private async Task<(BrigadeStoppage? Entity, List<StoppageRemark>? NewRemarks, string? Error, string? ErrorCode)> BuildStoppageAsync(
        BrigadeStoppage? existing,
        SaveBrigadeStoppageRequest request,
        CancellationToken cancellationToken)
    {
        var brigadeUser = await users.Users.AsNoTracking().FirstOrDefaultAsync(
            x => x.Id == request.BrigadeUserId,
            cancellationToken);
        if (brigadeUser is null || !brigadeUser.IsActive)
        {
            return (null, null, "Выберите участника рейтинга (подразделение с галочкой «Рейтинг бригад»).", "brigade_invalid");
        }

        // Менять на бригаду без флага нельзя; оставить текущую (даже без флага) — можно.
        var keepingSameBrigade = existing is not null && existing.BrigadeUserId == request.BrigadeUserId;
        if (!keepingSameBrigade && !brigadeUser.CanViewBrigadeRating)
        {
            return (null, null, "Выберите участника рейтинга (подразделение с галочкой «Рейтинг бригад»).", "brigade_invalid");
        }

        if (!await users.IsInRoleAsync(brigadeUser, AppRoles.Department))
        {
            return (null, null, "Участник рейтинга должен иметь роль «Подразделение».", "brigade_not_department");
        }

        var brigadeName = DisplayOf(brigadeUser);

        // Мастер: из персонала этой бригады (снимок ФИО) либо свободный текст / название бригады.
        Guid? masterPersonId = null;
        string masterName;
        if (request.MasterPersonId is Guid mpid && mpid != Guid.Empty)
        {
            var person = await db.PersonnelPeople.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == mpid && x.OwnerUserId == brigadeUser.Id, cancellationToken);
            if (person is null)
            {
                return (null, null, "Выбранный мастер не найден в персонале бригады.", "master_invalid");
            }

            // Неактивного можно оставить только если он уже был выбран в этой остановке.
            if (!person.IsActive && existing?.MasterPersonId != person.Id)
            {
                return (null, null, "Выбранный мастер неактивен.", "master_inactive");
            }

            masterPersonId = person.Id;
            masterName = person.FullName.Trim();
        }
        else
        {
            masterName = string.IsNullOrWhiteSpace(request.MasterName)
                ? brigadeName
                : request.MasterName.Trim();
        }

        // Работа: из справочника (снимок текста) или вручную; пустая строка допустима.
        Guid? workItemId = null;
        var isWorkManual = request.IsWorkManual || request.WorkItemId is null || request.WorkItemId == Guid.Empty;
        string workDescription;
        if (isWorkManual)
        {
            workDescription = (request.WorkDescription ?? string.Empty).Trim();
        }
        else
        {
            var workItem = await db.StoppageWorkItems.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == request.WorkItemId!.Value, cancellationToken);
            if (workItem is null)
            {
                return (null, null, "Выбранная работа не найдена в справочнике.", "work_item_invalid");
            }

            workItemId = workItem.Id;
            workDescription = workItem.Text.Trim();
        }

        // Колонка WorkDescription — varchar(500), текст справочника может быть длиннее.
        if (workDescription.Length > 500)
        {
            return (null, null, "Текст работы длиннее 500 символов.", "work_too_long");
        }

        // Кто остановил: подразделение (StoppedByUserId) + опционально сотрудник из его персонала.
        Guid? stoppedByPersonId = null;
        Guid? stoppedById = null;
        string stoppedByName;

        if (request.StoppedByUserId is Guid sid && sid != Guid.Empty)
        {
            var stopperDept = await users.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == sid, cancellationToken);
            if (stopperDept is null || !stopperDept.IsActive)
            {
                return (null, null, "Подразделение «кто остановил» не найдено.", "stopped_by_invalid");
            }

            if (!await users.IsInRoleAsync(stopperDept, AppRoles.Department))
            {
                return (null, null, "«Кто остановил» — только подразделение.", "stopped_by_not_department");
            }

            stoppedById = sid;

            if (request.StoppedByPersonId is Guid spid && spid != Guid.Empty)
            {
                var stopperPerson = await db.PersonnelPeople.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == spid && x.OwnerUserId == sid, cancellationToken);
                if (stopperPerson is null)
                {
                    return (null, null, "Сотрудник не найден в персонале выбранного подразделения.", "stopped_by_person_invalid");
                }

                if (!stopperPerson.IsActive && existing?.StoppedByPersonId != stopperPerson.Id)
                {
                    return (null, null, "Выбранный остановивший неактивен.", "stopped_by_person_inactive");
                }

                stoppedByPersonId = stopperPerson.Id;
                stoppedByName = stopperPerson.FullName.Trim();
            }
            else
            {
                // Только подразделение — снимок DisplayName или ручной текст (legacy).
                stoppedByName = string.IsNullOrWhiteSpace(request.StoppedByName)
                    ? DisplayOf(stopperDept)
                    : request.StoppedByName.Trim();
            }
        }
        else if (request.StoppedByPersonId is Guid spidOnly && spidOnly != Guid.Empty)
        {
            return (null, null, "Сначала выберите подразделение, которое остановило.", "stopped_by_dept_required");
        }
        else
        {
            stoppedByName = (request.StoppedByName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(stoppedByName))
            {
                return (null, null, "Укажите, кто остановил.", "stopped_by_required");
            }
        }

        var builtRemarks = await BuildRemarksAsync(request.Remarks, cancellationToken);
        if (builtRemarks.Error is not null)
        {
            return (null, null, builtRemarks.Error, builtRemarks.ErrorCode);
        }

        var entity = existing ?? new BrigadeStoppage
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUserId = CurrentUserId()
        };

        entity.BrigadeUserId = brigadeUser.Id;
        entity.BrigadeName = brigadeName;
        entity.MasterName = masterName;
        entity.MasterPersonId = masterPersonId;
        entity.ReportDate = request.ReportDate;
        entity.StoppedByUserId = stoppedById;
        entity.StoppedByPersonId = stoppedByPersonId;
        entity.StoppedByName = stoppedByName;
        entity.StoppedAt = request.StoppedAt;
        entity.RestartedAt = request.RestartedAt;
        entity.Hours = BrigadeStoppageHours.Compute(request.StoppedAt, request.RestartedAt);
        entity.WorkDescription = workDescription;
        entity.WorkItemId = workItemId;
        entity.IsWorkManual = isWorkManual;
        entity.LastModifiedAt = DateTimeOffset.UtcNow;
        entity.LastModifiedByUserId = CurrentUserId();

        return (entity, builtRemarks.Items, null, null);
    }

    private async Task<(List<StoppageRemark>? Items, string? Error, string? ErrorCode)> BuildRemarksAsync(
        List<SaveStoppageRemarkRequest>? requestRemarks,
        CancellationToken cancellationToken)
    {
        var raw = requestRemarks ?? [];
        if (raw.Count == 0)
        {
            return (null, "Добавьте хотя бы один пункт акта.", "remarks_required");
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

        var result = new List<StoppageRemark>();
        var order = 0;
        foreach (var r in raw)
        {
            var isManual = r.IsManual || r.ActItemId is null || r.ActItemId == Guid.Empty;
            string text;
            string comment;
            Guid? actItemId;

            if (isManual)
            {
                text = (r.Text ?? string.Empty).Trim();
                comment = string.Empty;
                actItemId = null;
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }
            }
            else
            {
                if (!actMap.TryGetValue(r.ActItemId!.Value, out var act))
                {
                    return (null, "Выбранный пункт акта не найден в справочнике.", "act_item_invalid");
                }

                text = act.Text.Trim();
                comment = (r.Comment ?? string.Empty).Trim();
                actItemId = act.Id;
            }

            order++;
            result.Add(new StoppageRemark
            {
                Id = Guid.NewGuid(),
                ActItemId = actItemId,
                Text = text,
                Comment = comment,
                IsManual = isManual,
                IsResolved = r.IsResolved,
                ResolvedAt = r.IsResolved ? DateTimeOffset.UtcNow : null,
                SortOrder = order
            });
        }

        if (result.Count == 0)
        {
            return (null, "Добавьте хотя бы один пункт акта.", "remarks_required");
        }

        return (result, null, null);
    }

    private static string FormatRemarkForExcel(int index, StoppageRemark r)
    {
        var text = r.Text.Trim();
        if (!r.IsManual && !string.IsNullOrWhiteSpace(r.Comment))
        {
            return $"{index}. {text} ({r.Comment.Trim()})";
        }

        return $"{index}. {text}";
    }

    private static StoppageActItemResponse MapActItem(StoppageActItem x) => new()
    {
        Id = x.Id,
        Text = x.Text,
        IsActive = x.IsActive,
        SortOrder = x.SortOrder
    };

    private static StoppageWorkItemResponse MapWorkItem(StoppageWorkItem x) => new()
    {
        Id = x.Id,
        Text = x.Text,
        IsActive = x.IsActive,
        SortOrder = x.SortOrder
    };

    private static StoppageRemarkResponse MapRemark(StoppageRemark r) => new()
    {
        Id = r.Id,
        ActItemId = r.ActItemId,
        Text = r.Text,
        Comment = r.Comment,
        IsManual = r.IsManual,
        IsResolved = r.IsResolved,
        ResolvedAt = r.ResolvedAt,
        SortOrder = r.SortOrder
    };

    private static BrigadeStoppageListItemResponse MapListItem(BrigadeStoppage x) => new()
    {
        Id = x.Id,
        ReportDate = x.ReportDate,
        BrigadeUserId = x.BrigadeUserId,
        BrigadeName = x.BrigadeName,
        MasterName = x.MasterName,
        MasterPersonId = x.MasterPersonId,
        StoppedByName = x.StoppedByName,
        StoppedByUserId = x.StoppedByUserId,
        StoppedByPersonId = x.StoppedByPersonId,
        StoppedAt = x.StoppedAt,
        RestartedAt = x.RestartedAt,
        Hours = x.Hours,
        WorkDescription = x.WorkDescription,
        WorkItemId = x.WorkItemId,
        IsWorkManual = x.IsWorkManual,
        RemarkCount = x.Remarks.Count,
        UnresolvedRemarkCount = x.Remarks.Count(r => !r.IsResolved)
    };

    private static BrigadeStoppageDetailResponse MapDetail(BrigadeStoppage x)
    {
        var item = MapListItem(x);
        return new BrigadeStoppageDetailResponse
        {
            Id = item.Id,
            ReportDate = item.ReportDate,
            BrigadeUserId = item.BrigadeUserId,
            BrigadeName = item.BrigadeName,
            MasterName = item.MasterName,
            MasterPersonId = item.MasterPersonId,
            StoppedByName = item.StoppedByName,
            StoppedByUserId = item.StoppedByUserId,
            StoppedByPersonId = item.StoppedByPersonId,
            StoppedAt = item.StoppedAt,
            RestartedAt = item.RestartedAt,
            Hours = item.Hours,
            WorkDescription = item.WorkDescription,
            WorkItemId = item.WorkItemId,
            IsWorkManual = item.IsWorkManual,
            RemarkCount = item.RemarkCount,
            UnresolvedRemarkCount = item.UnresolvedRemarkCount,
            Remarks = x.Remarks
                .OrderBy(r => r.SortOrder)
                .ThenBy(r => r.Text)
                .Select(MapRemark)
                .ToList()
        };
    }

    private IQueryable<BrigadeStoppage> ScopeStoppages(IQueryable<BrigadeStoppage> q, bool seeAll)
    {
        if (seeAll)
        {
            return q;
        }

        var uid = CurrentUserId();
        return q.Where(x => x.BrigadeUserId == uid);
    }

    private (DateOnly Start, DateOnly End) ResolvePeriod(DateOnly? from, DateOnly? to)
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

    /// <summary>Пресеты как на Сводке; from/to без period → custom. «Сегодня» — AppTimeZone.</summary>
    private (string Period, DateOnly Start, DateOnly End) ResolveDashboardPeriod(
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

        // Явные даты без пресета — произвольный период.
        if (from is not null || to is not null)
        {
            var (start, end) = ResolvePeriod(from, to);
            return ("custom", start, end);
        }

        return ResolveMonthPreset(today, year, month);
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

    private static (DateOnly Start, DateOnly End) PreviousPeriodBounds(
        string period,
        DateOnly start,
        DateOnly end)
    {
        if (period == "today")
        {
            return (start.AddDays(-1), start.AddDays(-1));
        }

        if (period == "month")
        {
            var prevStart = start.AddMonths(-1);
            return (prevStart, prevStart.AddMonths(1).AddDays(-1));
        }

        if (period == "year")
        {
            return (new DateOnly(start.Year - 1, 1, 1), new DateOnly(start.Year - 1, 12, 31));
        }

        var days = end.DayNumber - start.DayNumber + 1;
        var prevEnd = start.AddDays(-1);
        return (prevEnd.AddDays(-(days - 1)), prevEnd);
    }

    private static decimal Percent(int part, int total) =>
        total <= 0 ? 0m : Math.Round(part * 100m / total, 1);

    private static decimal AverageHours(List<BrigadeStoppage> rows)
    {
        var withRestart = rows.Where(x => x.RestartedAt is not null).ToList();
        if (withRestart.Count == 0)
        {
            return 0m;
        }

        return Math.Round(withRestart.Average(x => x.Hours), 2);
    }

    private static List<BrigadeDailyTrendPointResponse> BuildDailyTrend(
        string period,
        DateOnly start,
        DateOnly end,
        List<BrigadeStoppage> rows)
    {
        if (period == "year")
        {
            var byMonth = rows.GroupBy(x => new DateOnly(x.ReportDate.Year, x.ReportDate.Month, 1))
                .ToDictionary(g => g.Key, g => g.ToList());
            var points = new List<BrigadeDailyTrendPointResponse>();
            for (var m = 1; m <= 12; m++)
            {
                var monthStart = new DateOnly(start.Year, m, 1);
                if (monthStart > end)
                {
                    break;
                }

                byMonth.TryGetValue(monthStart, out var bucket);
                bucket ??= [];
                var remarks = bucket.SelectMany(x => x.Remarks).ToList();
                points.Add(new BrigadeDailyTrendPointResponse
                {
                    Date = monthStart,
                    Label = monthStart.ToString("MM"),
                    StoppageCount = bucket.Count,
                    Hours = bucket.Sum(x => x.Hours),
                    RemarkCount = remarks.Count,
                    UnresolvedRemarkCount = remarks.Count(r => !r.IsResolved)
                });
            }

            return points;
        }

        var byDay = rows.GroupBy(x => x.ReportDate).ToDictionary(g => g.Key, g => g.ToList());
        var daily = new List<BrigadeDailyTrendPointResponse>();
        for (var day = start; day <= end; day = day.AddDays(1))
        {
            byDay.TryGetValue(day, out var bucket);
            bucket ??= [];
            var remarks = bucket.SelectMany(x => x.Remarks).ToList();
            var dense = period is "month" || (end.DayNumber - start.DayNumber) > 14;
            daily.Add(new BrigadeDailyTrendPointResponse
            {
                Date = day,
                Label = dense ? day.Day.ToString() : day.ToString("dd.MM"),
                StoppageCount = bucket.Count,
                Hours = bucket.Sum(x => x.Hours),
                RemarkCount = remarks.Count,
                UnresolvedRemarkCount = remarks.Count(r => !r.IsResolved)
            });
        }

        return daily;
    }

    private async Task<bool> CanViewAsync(CancellationToken cancellationToken)
    {
        if (IsStaff())
        {
            return true;
        }

        if (!User.IsInRole(AppRoles.Department))
        {
            return false;
        }

        var user = await users.GetUserAsync(User);
        return user is { IsActive: true }
            && (user.CanViewBrigadeRating || user.CanOperateBrigadeRating);
    }

    private async Task<bool> CanOperateAsync(CancellationToken cancellationToken)
    {
        if (IsStaff())
        {
            return true;
        }

        if (!User.IsInRole(AppRoles.Department))
        {
            return false;
        }

        var user = await users.GetUserAsync(User);
        return user is { IsActive: true, CanOperateBrigadeRating: true };
    }

    /// <summary>UserManager шарит DbContext — иначе SaveChanges может UPDATE AspNetUsers по stale ConcurrencyStamp.</summary>
    private void DetachTrackedUsers()
    {
        foreach (var entry in db.ChangeTracker.Entries<ApplicationUser>().ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private bool IsStaff() =>
        User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher);

    private Guid? CurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static string DisplayOf(ApplicationUser u) =>
        string.IsNullOrWhiteSpace(u.DisplayName) ? (u.UserName ?? string.Empty) : u.DisplayName;
}
