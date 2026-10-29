using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.Requests;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Attachments;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;
using Crown.Infrastructure.Time;

namespace Crown.Features.Requests;

[ApiController]
[Route("api/requests")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class RequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttachmentStorageService _attachments;
    private readonly AttachmentPreviewService _preview;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppSettingsService _settings;
    private readonly AppTimeZone _time;

    public RequestsController(
        AppDbContext db,
        AttachmentStorageService attachments,
        AttachmentPreviewService preview,
        UserManager<ApplicationUser> userManager,
        AppSettingsService settings,
        AppTimeZone time)
    {
        _db = db;
        _attachments = attachments;
        _preview = preview;
        _userManager = userManager;
        _settings = settings;
        _time = time;
    }

    /// <summary>Активные пользователи с ролью Initiator — выбор исполнителя и фильтры.</summary>
    [HttpGet("assignees")]
    public async Task<ActionResult<List<RequestAssigneeResponse>>> GetAssignees(
        CancellationToken cancellationToken)
    {
        var users = await _userManager.GetUsersInRoleAsync(AppRoles.Initiator);
        var items = users
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.UserName)
            .Select(x => new RequestAssigneeResponse
            {
                Id = x.Id,
                UserName = x.UserName ?? string.Empty,
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName)
                    ? (x.UserName ?? string.Empty)
                    : x.DisplayName
            })
            .ToList();

        return Ok(items);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ServiceRequestListItemResponse>>> GetList(
        [FromQuery] string? search,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromQuery] bool? includeArchived = null,
        [FromQuery] string? sort = null,
        [FromQuery] string? dir = null,
        [FromQuery] int? status = null,
        [FromQuery] Guid? initiatorUserId = null,
        [FromQuery] Guid? executorUserId = null,
        [FromQuery] bool? acknowledged = null,
        CancellationToken cancellationToken = default)
    {
        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);
        // null → настройка «Скрывать архивные по умолчанию»
        var showArchived = includeArchived
            ?? !await _settings.GetHideArchivedByDefaultAsync(cancellationToken);

        // Глобальный query filter режет архив — для include нужен IgnoreQueryFilters.
        var query = showArchived
            ? _db.ServiceRequests.IgnoreQueryFilters().AsNoTracking()
            : _db.ServiceRequests.AsNoTracking();

        query = ApplyVisibility(query);

        if (status is int st && Enum.IsDefined(typeof(ServiceRequestStatus), st))
        {
            query = query.Where(x => x.Status == (ServiceRequestStatus)st);
        }

        if (initiatorUserId is Guid iid && iid != Guid.Empty)
        {
            query = query.Where(x => x.InitiatorUserId == iid);
        }

        if (executorUserId is Guid eid && eid != Guid.Empty)
        {
            query = query.Where(x => x.ExecutorUserId == eid);
        }

        if (acknowledged is bool ack)
        {
            query = query.Where(x => x.Acknowledged == ack);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = long.TryParse(term, out var id)
                ? query.Where(x => x.Id == id
                    || x.InitiatorName.Contains(term)
                    || x.ExecutorName.Contains(term)
                    || x.Phone.Contains(term)
                    || x.Body.Contains(term))
                : query.Where(x => x.InitiatorName.Contains(term)
                    || x.ExecutorName.Contains(term)
                    || x.Phone.Contains(term)
                    || x.Body.Contains(term));
        }

        var asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);
        var sortKey = (sort ?? "id").Trim().ToLowerInvariant();
        query = sortKey switch
        {
            "initiator" => asc
                ? query.OrderBy(x => x.InitiatorName).ThenByDescending(x => x.Id)
                : query.OrderByDescending(x => x.InitiatorName).ThenByDescending(x => x.Id),
            "executor" or "department" => asc
                ? query.OrderBy(x => x.ExecutorName).ThenByDescending(x => x.Id)
                : query.OrderByDescending(x => x.ExecutorName).ThenByDescending(x => x.Id),
            _ => asc
                ? query.OrderBy(x => x.Id)
                : query.OrderByDescending(x => x.Id)
        };

        // Имена: живые DisplayName, если пользователь ещё есть; иначе снимок.
        var response = await (
            from x in query
            join iu in _db.Users.AsNoTracking() on x.InitiatorUserId equals iu.Id into iuj
            from iu in iuj.DefaultIfEmpty()
            join eu in _db.Users.AsNoTracking() on x.ExecutorUserId equals eu.Id into euj
            from eu in euj.DefaultIfEmpty()
            select new ServiceRequestListItemResponse
            {
                Id = x.Id,
                Status = x.Status,
                StatusLabel = StatusLabel(x.Status),
                Acknowledged = x.Acknowledged,
                InitiatorUserId = x.InitiatorUserId,
                InitiatorName = iu != null
                    ? (iu.DisplayName != "" ? iu.DisplayName : iu.UserName ?? x.InitiatorName)
                    : x.InitiatorName,
                ExecutorUserId = x.ExecutorUserId,
                ExecutorName = eu != null
                    ? (eu.DisplayName != "" ? eu.DisplayName : eu.UserName ?? x.ExecutorName)
                    : x.ExecutorName,
                SubmittedAt = x.SubmittedAt,
                ReactionAt = x.ReactionAt,
                CompletedAt = x.CompletedAt,
                Phone = x.Phone,
                BodyPreview = x.Body.Length > 120 ? x.Body.Substring(0, 120) + "…" : x.Body,
                DueAt = x.DueAt,
                HasComplaint = x.ComplaintText != null && x.ComplaintText != "",
                IsArchived = x.IsArchived
            })
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

    /// <summary>
    /// Очередь «Сегодня»: заявки с действием для текущего пользователя.
    /// Исключаем выполненные/отменённые; Returned только если пользователь — инициатор
    /// (возврат исполнителем уже сделан — во входящих не показываем).
    /// Срок: сегодня и дальше + просроченные (ещё требуют действия).
    /// </summary>
    [HttpGet("today")]
    public async Task<ActionResult<TodayRequestsResponse>> GetToday(
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var today = _time.GetToday();

        // Архив режется query filter. Терминальные статусы — нет действия.
        var baseQuery = _db.ServiceRequests.AsNoTracking()
            .Where(x => x.Status != ServiceRequestStatus.Completed
                        && x.Status != ServiceRequestStatus.Cancelled);

        // Входящие: я исполнитель; Returned не берём — возврат уже сделан исполнителем.
        var incomingQuery = baseQuery.Where(x =>
            x.ExecutorUserId == userId
            && x.Status != ServiceRequestStatus.Returned);

        // Исходящие: я инициатор (Returned показываем — надо править/переподать).
        var outgoingQuery = baseQuery.Where(x => x.InitiatorUserId == userId);

        var incoming = await ProjectListItems(incomingQuery.OrderBy(x => x.DueAt).ThenBy(x => x.Id))
            .ToListAsync(cancellationToken);
        var outgoing = await ProjectListItems(outgoingQuery.OrderBy(x => x.DueAt).ThenBy(x => x.Id))
            .ToListAsync(cancellationToken);

        return Ok(new TodayRequestsResponse
        {
            Date = today,
            Incoming = incoming,
            Outgoing = outgoing
        });
    }

    private IQueryable<ServiceRequestListItemResponse> ProjectListItems(
        IQueryable<ServiceRequest> query) =>
        from x in query
        join iu in _db.Users.AsNoTracking() on x.InitiatorUserId equals iu.Id into iuj
        from iu in iuj.DefaultIfEmpty()
        join eu in _db.Users.AsNoTracking() on x.ExecutorUserId equals eu.Id into euj
        from eu in euj.DefaultIfEmpty()
        select new ServiceRequestListItemResponse
        {
            Id = x.Id,
            Status = x.Status,
            StatusLabel = StatusLabel(x.Status),
            Acknowledged = x.Acknowledged,
            InitiatorUserId = x.InitiatorUserId,
            InitiatorName = iu != null
                ? (iu.DisplayName != "" ? iu.DisplayName : iu.UserName ?? x.InitiatorName)
                : x.InitiatorName,
            ExecutorUserId = x.ExecutorUserId,
            ExecutorName = eu != null
                ? (eu.DisplayName != "" ? eu.DisplayName : eu.UserName ?? x.ExecutorName)
                : x.ExecutorName,
            SubmittedAt = x.SubmittedAt,
            ReactionAt = x.ReactionAt,
            CompletedAt = x.CompletedAt,
            Phone = x.Phone,
            BodyPreview = x.Body.Length > 120 ? x.Body.Substring(0, 120) + "…" : x.Body,
            DueAt = x.DueAt,
            HasComplaint = x.ComplaintText != null && x.ComplaintText != "",
            IsArchived = x.IsArchived
        };

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> GetById(
        long id,
        CancellationToken cancellationToken)
    {
        var entity = await LoadAccessibleAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        // Ознакомление: исполнитель открыл карточку → зелёная точка.
        var userId = GetUserId();
        if (userId is not null
            && entity.ExecutorUserId == userId
            && !entity.Acknowledged)
        {
            entity.Acknowledged = true;
            entity.LastModifiedAt = DateTimeOffset.UtcNow;
            entity.LastModifiedByUserId = userId;
            await _db.SaveChangesAsync(cancellationToken);
        }

        return Ok(await MapDetailAsync(entity, cancellationToken));
    }

    [HttpPost]
    public async Task<ActionResult<ServiceRequestDetailResponse>> Create(
        [FromBody] CreateServiceRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!PhoneNumberNormalizer.TryNormalize(request.Phone, out var phone, out var phoneError))
        {
            return BadRequest(new { error = "phone_invalid", message = phoneError });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "body_required", message = "Текст заявки обязателен." });
        }

        ApplicationUser? initiatorUser;
        if (User.IsInRole(AppRoles.Administrator))
        {
            if (request.InitiatorUserId is null || request.InitiatorUserId == Guid.Empty)
            {
                return BadRequest(new { error = "initiator_required", message = "Выберите инициатора." });
            }

            initiatorUser = await _userManager.Users
                .FirstOrDefaultAsync(x => x.Id == request.InitiatorUserId && x.IsActive, cancellationToken);
            if (initiatorUser is null)
            {
                return BadRequest(new { error = "initiator_invalid", message = "Пользователь-инициатор не найден." });
            }
        }
        else
        {
            initiatorUser = await _userManager.GetUserAsync(User);
            if (initiatorUser is null || !initiatorUser.IsActive)
            {
                return BadRequest(new { error = "initiator_required", message = "Не удалось определить инициатора." });
            }
        }

        var executorUser = await ResolveActiveInitiatorAsync(request.ExecutorUserId, cancellationToken);
        if (executorUser is null)
        {
            return BadRequest(new
            {
                error = "executor_invalid",
                message = "Выберите активного исполнителя с ролью «Инициатор»."
            });
        }

        var initiatorName = DisplayOf(initiatorUser);
        var executorName = DisplayOf(executorUser);
        if (string.IsNullOrWhiteSpace(initiatorName))
        {
            return BadRequest(new { error = "initiator_required", message = "У пользователя не задано отображаемое имя." });
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new ServiceRequest
        {
            SubmittedAt = now,
            Phone = phone,
            DueAt = request.DueAt.ToUniversalTime(),
            InitiatorName = initiatorName,
            InitiatorUserId = initiatorUser.Id,
            ExecutorName = executorName,
            ExecutorUserId = executorUser.Id,
            TransportType = string.IsNullOrWhiteSpace(request.TransportType) ? null : request.TransportType.Trim(),
            Body = request.Body.Trim(),
            Status = ServiceRequestStatus.Submitted,
            CreatedByUserId = GetUserId(),
            LastModifiedAt = now,
            LastModifiedByUserId = GetUserId()
        };

        _db.ServiceRequests.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, await MapDetailAsync(entity, cancellationToken));
    }

    [HttpPost("{id:long}/transition")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> Transition(
        long id,
        [FromBody] TransitionServiceRequestRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.ServiceRequests
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!CanAccess(entity))
        {
            return NotFound();
        }

        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        var userId = GetUserId();
        var isInitiator = userId is Guid uid && entity.InitiatorUserId == uid;
        var canManage = CanManageWorkflow(entity);
        // Инициатор: отмена своей заявки / повторная подача после возврата.
        var initiatorOnlyAction = action is "cancel" or "resubmit";
        if (!canManage && !(initiatorOnlyAction && isInitiator))
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(request.RowVersion)
            && uint.TryParse(request.RowVersion, out var expectedXmin)
            && entity.Xmin != expectedXmin)
        {
            return Conflict(new { error = "concurrency", message = "Заявка изменена другим пользователем." });
        }

        var comment = string.IsNullOrWhiteSpace(request.ExecutorComment)
            ? entity.ExecutorComment
            : request.ExecutorComment.Trim();
        // Админ может менять статус даже у закрытых заявок (обход TryEnsureStatus).
        var isAdmin = User.IsInRole(AppRoles.Administrator);

        switch (action)
        {
            case "accept":
                if (!isAdmin
                    && !TryEnsureStatus(entity, out var acceptErr, ServiceRequestStatus.Submitted, ServiceRequestStatus.Returned))
                {
                    return BadRequest(new { error = "invalid_status", message = acceptErr });
                }

                entity.Status = ServiceRequestStatus.InProgress;
                entity.ReactionAt ??= DateTimeOffset.UtcNow;
                entity.CompletedAt = null;
                entity.Acknowledged = true;
                break;
            case "reject":
                if (!isAdmin
                    && !TryEnsureStatus(entity, out var rejectErr, ServiceRequestStatus.Submitted, ServiceRequestStatus.InProgress, ServiceRequestStatus.Returned))
                {
                    return BadRequest(new { error = "invalid_status", message = rejectErr });
                }

                if (string.IsNullOrWhiteSpace(request.ExecutorComment))
                {
                    return BadRequest(new { error = "comment_required", message = "Чтобы отклонить заявку, нужен комментарий." });
                }

                entity.Status = ServiceRequestStatus.Rejected;
                entity.ReactionAt ??= DateTimeOffset.UtcNow;
                entity.CompletedAt = DateTimeOffset.UtcNow;
                break;
            case "return":
                if (!isAdmin
                    && !TryEnsureStatus(entity, out var returnErr, ServiceRequestStatus.Submitted, ServiceRequestStatus.InProgress))
                {
                    return BadRequest(new { error = "invalid_status", message = returnErr });
                }

                if (string.IsNullOrWhiteSpace(request.ExecutorComment))
                {
                    return BadRequest(new { error = "comment_required", message = "Чтобы вернуть заявку, нужен комментарий." });
                }

                entity.Status = ServiceRequestStatus.Returned;
                entity.ReactionAt ??= DateTimeOffset.UtcNow;
                entity.CompletedAt = null;
                break;
            case "complete":
                if (!isAdmin
                    && !TryEnsureStatus(entity, out var completeErr, ServiceRequestStatus.InProgress))
                {
                    return BadRequest(new { error = "invalid_status", message = completeErr });
                }

                entity.Status = ServiceRequestStatus.Completed;
                entity.CompletedAt = DateTimeOffset.UtcNow;
                break;
            case "cancel":
                // Инициатор может отменить свою заявку (подана / в работе / возвращена).
                if (!isAdmin
                    && !TryEnsureStatus(entity, out var cancelErr,
                        ServiceRequestStatus.Submitted,
                        ServiceRequestStatus.InProgress,
                        ServiceRequestStatus.Returned))
                {
                    return BadRequest(new { error = "invalid_status", message = cancelErr });
                }

                if (!canManage && !isInitiator)
                {
                    return Forbid();
                }

                entity.Status = ServiceRequestStatus.Cancelled;
                entity.CompletedAt = DateTimeOffset.UtcNow;
                break;
            case "resubmit":
                // Инициатор (или админ) правит текст и снова подаёт возвращённую заявку.
                if (!isInitiator && !isAdmin)
                {
                    return Forbid();
                }

                if (!TryEnsureStatus(entity, out var resubmitErr, ServiceRequestStatus.Returned))
                {
                    return BadRequest(new { error = "invalid_status", message = resubmitErr });
                }

                if (string.IsNullOrWhiteSpace(request.Body))
                {
                    return BadRequest(new { error = "body_required", message = "Укажите текст заявки." });
                }

                entity.Body = request.Body.Trim();
                entity.Status = ServiceRequestStatus.Submitted;
                entity.Acknowledged = false;
                entity.CompletedAt = null;
                break;
            case "reschedule":
                if (!isAdmin
                    && !TryEnsureStatus(entity, out var reschedErr, ServiceRequestStatus.InProgress))
                {
                    return BadRequest(new { error = "invalid_status", message = reschedErr });
                }

                if (request.NewDueAt is null)
                {
                    return BadRequest(new { error = "due_required", message = "Укажите новый срок." });
                }

                if (string.IsNullOrWhiteSpace(request.ExecutorComment))
                {
                    return BadRequest(new { error = "comment_required", message = "Для переноса срока нужен комментарий." });
                }

                entity.DueAt = request.NewDueAt.Value.ToUniversalTime();
                break;
            case "acknowledge":
                // Ручная отметка — только администратор (авто-ознак. при открытии — отдельно в GetById).
                if (!isAdmin)
                {
                    return Forbid();
                }

                entity.Acknowledged = request.Acknowledged ?? true;
                break;
            default:
                return BadRequest(new { error = "action_unknown", message = "Неизвестное действие." });
        }

        entity.ExecutorComment = comment;
        entity.LastModifiedAt = DateTimeOffset.UtcNow;
        entity.LastModifiedByUserId = GetUserId();
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(await MapDetailAsync(entity, cancellationToken));
    }

    /// <summary>Жалоба инициатора на возврат / отклонение / выполненную (один раз).</summary>
    [HttpPost("{id:long}/complaint")]
    public async Task<ActionResult<ServiceRequestDetailResponse>> FileComplaint(
        long id,
        [FromBody] FileComplaintRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await LoadAccessibleAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var userId = GetUserId();
        if (userId is null || entity.InitiatorUserId != userId.Value)
        {
            return Forbid();
        }

        if (entity.Status is not (
            ServiceRequestStatus.Returned
            or ServiceRequestStatus.Rejected
            or ServiceRequestStatus.Completed))
        {
            return BadRequest(new
            {
                error = "invalid_status",
                message = "Жалобу можно подать по возвращённой, отклонённой или выполненной заявке."
            });
        }

        if (!string.IsNullOrWhiteSpace(entity.ComplaintText))
        {
            return BadRequest(new { error = "complaint_exists", message = "Жалоба по этой заявке уже подана." });
        }

        if (!request.Confirmed)
        {
            return BadRequest(new { error = "confirm_required", message = "Подтвердите, что жалоба обоснована." });
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { error = "text_required", message = "Опишите суть жалобы." });
        }

        var user = await _userManager.GetUserAsync(User);
        var name = user is null
            ? entity.InitiatorName
            : DisplayOf(user);

        entity.ComplaintText = request.Text.Trim();
        entity.ComplaintAt = DateTimeOffset.UtcNow;
        entity.ComplaintByUserId = userId;
        entity.ComplaintByName = name;
        entity.LastModifiedAt = DateTimeOffset.UtcNow;
        entity.LastModifiedByUserId = userId;
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(await MapDetailAsync(entity, cancellationToken));
    }

    [HttpPost("{id:long}/attachments")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<RequestAttachmentResponse>> UploadAttachment(
        long id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var entity = await LoadAccessibleAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "file_required", message = "Файл не передан." });
        }

        if (file.Length > _attachments.MaxFileBytes)
        {
            return BadRequest(new { error = "file_too_large", message = "Файл слишком большой." });
        }

        await using var stream = file.OpenReadStream();
        var (relative, size) = await _attachments.SaveAsync(id, file.FileName, stream, cancellationToken);
        var attachment = new RequestAttachment
        {
            Id = Guid.NewGuid(),
            ServiceRequestId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            SizeBytes = size,
            StoragePath = relative,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedByUserId = GetUserId()
        };
        _db.RequestAttachments.Add(attachment);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new RequestAttachmentResponse
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            UploadedAt = attachment.UploadedAt
        });
    }

    [HttpGet("{id:long}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> DownloadAttachment(
        long id,
        Guid attachmentId,
        [FromQuery] bool inline = false,
        CancellationToken cancellationToken = default)
    {
        var entity = await LoadAccessibleAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var attachment = entity.Attachments.FirstOrDefault(x => x.Id == attachmentId);
        if (attachment is null)
        {
            return NotFound();
        }

        var path = _attachments.ResolveExistingPath(attachment.StoragePath);
        if (path is null)
        {
            return NotFound();
        }

        // inline=true — для object/img в превью (без Content-Disposition: attachment).
        if (inline)
        {
            return PhysicalFile(path, attachment.ContentType);
        }

        return PhysicalFile(path, attachment.ContentType, attachment.FileName);
    }

    [HttpGet("{id:long}/attachments/{attachmentId:guid}/preview")]
    public async Task<ActionResult<AttachmentPreviewResponse>> PreviewAttachment(
        long id,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var entity = await LoadAccessibleAsync(id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        var attachment = entity.Attachments.FirstOrDefault(x => x.Id == attachmentId);
        if (attachment is null)
        {
            return NotFound();
        }

        var path = _attachments.ResolveExistingPath(attachment.StoragePath);
        if (path is null)
        {
            return NotFound();
        }

        await using var stream = global::System.IO.File.OpenRead(path);
        var preview = _preview.Build(attachment.FileName, attachment.ContentType, stream);
        return Ok(preview);
    }

    private IQueryable<ServiceRequest> ApplyVisibility(IQueryable<ServiceRequest> query)
    {
        if (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher))
        {
            return query;
        }

        var userId = GetUserId();
        // Свои поданные + назначенные мне как исполнителю.
        return query.Where(x =>
            x.InitiatorUserId == userId
            || x.ExecutorUserId == userId
            || x.CreatedByUserId == userId);
    }

    private bool CanAccess(ServiceRequest entity)
    {
        if (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher))
        {
            return true;
        }

        var userId = GetUserId();
        return userId is not null
            && (entity.InitiatorUserId == userId
                || entity.ExecutorUserId == userId
                || entity.CreatedByUserId == userId);
    }

    /// <summary>Workflow: админ / диспетчер (прозапас) / исполнитель заявки.</summary>
    private bool CanManageWorkflow(ServiceRequest entity)
    {
        if (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher))
        {
            return true;
        }

        var userId = GetUserId();
        return userId is not null && entity.ExecutorUserId == userId;
    }

    private async Task<ServiceRequest?> LoadAccessibleAsync(long id, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters — чтобы открыть архивную заявку по прямой ссылке.
        var entity = await _db.ServiceRequests
            .IgnoreQueryFilters()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null || !CanAccess(entity))
        {
            return null;
        }

        return entity;
    }

    /// <summary>Мягкая архивация — инициатор своей заявки (или админ/диспетчер).</summary>
    [HttpPatch("{id:long}/archive")]
    public Task<IActionResult> Archive(long id, CancellationToken cancellationToken) =>
        SetArchivedAsync(id, archived: true, cancellationToken);

    [HttpPatch("{id:long}/unarchive")]
    public Task<IActionResult> Unarchive(long id, CancellationToken cancellationToken) =>
        SetArchivedAsync(id, archived: false, cancellationToken);

    /// <summary>Безвозвратное удаление: админ всегда; диспетчер — если галочка в настройках.</summary>
    [HttpDelete("{id:long}")]
    [Authorize(Policy = AuthorizationPolicies.HardDeleteRecords)]
    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var entity = await _db.ServiceRequests
            .IgnoreQueryFilters()
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null || !CanAccess(entity))
        {
            return NotFound();
        }

        foreach (var a in entity.Attachments)
        {
            _attachments.DeleteIfExists(a.StoragePath);
        }

        _db.ServiceRequests.Remove(entity);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<IActionResult> SetArchivedAsync(long id, bool archived, CancellationToken cancellationToken)
    {
        var entity = await _db.ServiceRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null || !CanAccess(entity))
        {
            return NotFound();
        }

        if (!CanArchive(entity))
        {
            return Forbid();
        }

        if (entity.IsArchived != archived)
        {
            entity.IsArchived = archived;
            entity.LastModifiedAt = DateTimeOffset.UtcNow;
            entity.LastModifiedByUserId = GetUserId();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    /// <summary>Архив: инициатор заявки или админ/диспетчер.</summary>
    private bool CanArchive(ServiceRequest entity)
    {
        if (User.IsInRole(AppRoles.Administrator) || User.IsInRole(AppRoles.Dispatcher))
        {
            return true;
        }

        var userId = GetUserId();
        return userId is not null && entity.InitiatorUserId == userId;
    }

    private async Task<ApplicationUser?> ResolveActiveInitiatorAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        var user = await _userManager.Users
            .FirstOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);
        if (user is null)
        {
            return null;
        }

        return await _userManager.IsInRoleAsync(user, AppRoles.Initiator) ? user : null;
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static string DisplayOf(ApplicationUser user) =>
        string.IsNullOrWhiteSpace(user.DisplayName)
            ? (user.UserName ?? string.Empty)
            : user.DisplayName;

    private static bool TryEnsureStatus(
        ServiceRequest entity,
        out string? error,
        params ServiceRequestStatus[] allowed)
    {
        if (allowed.Contains(entity.Status))
        {
            error = null;
            return true;
        }

        error = $"Действие недоступно в статусе «{StatusLabel(entity.Status)}».";
        return false;
    }

    private async Task<ServiceRequestDetailResponse> MapDetailAsync(
        ServiceRequest x,
        CancellationToken cancellationToken)
    {
        var initiatorName = x.InitiatorName;
        if (x.InitiatorUserId is Guid iid)
        {
            var u = await _userManager.Users.AsNoTracking()
                .Where(user => user.Id == iid)
                .Select(user => new { user.DisplayName, user.UserName })
                .FirstOrDefaultAsync(cancellationToken);
            if (u is not null)
            {
                initiatorName = string.IsNullOrWhiteSpace(u.DisplayName) ? (u.UserName ?? initiatorName) : u.DisplayName;
            }
        }

        var executorName = x.ExecutorName;
        if (x.ExecutorUserId is Guid eid)
        {
            var u = await _userManager.Users.AsNoTracking()
                .Where(user => user.Id == eid)
                .Select(user => new { user.DisplayName, user.UserName })
                .FirstOrDefaultAsync(cancellationToken);
            if (u is not null)
            {
                executorName = string.IsNullOrWhiteSpace(u.DisplayName) ? (u.UserName ?? executorName) : u.DisplayName;
            }
        }

        return new ServiceRequestDetailResponse
        {
            Id = x.Id,
            Status = x.Status,
            StatusLabel = StatusLabel(x.Status),
            Acknowledged = x.Acknowledged,
            SubmittedAt = x.SubmittedAt,
            Phone = x.Phone,
            DueAt = x.DueAt,
            InitiatorName = initiatorName,
            InitiatorUserId = x.InitiatorUserId,
            ExecutorName = executorName,
            ExecutorUserId = x.ExecutorUserId,
            TransportType = x.TransportType,
            Body = x.Body,
            ExecutorComment = x.ExecutorComment,
            ReactionAt = x.ReactionAt,
            CompletedAt = x.CompletedAt,
            RowVersion = x.Xmin.ToString(),
            HasComplaint = !string.IsNullOrWhiteSpace(x.ComplaintText),
            ComplaintText = x.ComplaintText,
            ComplaintAt = x.ComplaintAt,
            ComplaintByName = x.ComplaintByName,
            IsArchived = x.IsArchived,
            Attachments = x.Attachments
                .OrderByDescending(a => a.UploadedAt)
                .Select(a => new RequestAttachmentResponse
                {
                    Id = a.Id,
                    FileName = a.FileName,
                    ContentType = a.ContentType,
                    SizeBytes = a.SizeBytes,
                    UploadedAt = a.UploadedAt
                })
                .ToList()
        };
    }

    internal static string StatusLabel(ServiceRequestStatus status) => status switch
    {
        ServiceRequestStatus.Submitted => "Подана",
        ServiceRequestStatus.InProgress => "В работе",
        ServiceRequestStatus.Rejected => "Отклонена",
        ServiceRequestStatus.Returned => "Возвращена",
        ServiceRequestStatus.Completed => "Выполнена",
        ServiceRequestStatus.Cancelled => "Отменена",
        _ => status.ToString()
    };
}
