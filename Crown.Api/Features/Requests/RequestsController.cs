using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
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

namespace Crown.Features.Requests;

[ApiController]
[Route("api/requests")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class RequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AttachmentStorageService _attachments;

    public RequestsController(AppDbContext db, AttachmentStorageService attachments)
    {
        _db = db;
        _attachments = attachments;
    }

    [HttpGet("departments")]
    public async Task<ActionResult<List<ServiceDepartmentResponse>>> GetDepartments(
        CancellationToken cancellationToken)
    {
        var items = await _db.ServiceDepartments.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new ServiceDepartmentResponse { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<ServiceRequestListItemResponse>>> GetList(
        [FromQuery] string? search,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromQuery] bool includeArchived = false,
        CancellationToken cancellationToken = default)
    {
        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);
        var query = _db.ServiceRequests.AsNoTracking().AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(x => !x.IsArchived);
        }

        // Инициатор без диспетчерских прав видит только свои заявки.
        if (User.IsInRole(AppRoles.Initiator)
            && !User.IsInRole(AppRoles.Dispatcher)
            && !User.IsInRole(AppRoles.Administrator))
        {
            var userId = GetUserId();
            query = query.Where(x => x.CreatedByUserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = long.TryParse(term, out var id)
                ? query.Where(x => x.Id == id
                    || x.InitiatorName.Contains(term)
                    || x.Phone.Contains(term)
                    || x.Body.Contains(term))
                : query.Where(x => x.InitiatorName.Contains(term)
                    || x.Phone.Contains(term)
                    || x.Body.Contains(term)
                    || x.ServiceDepartment!.Name.Contains(term));
        }

        var response = await query
            .OrderByDescending(x => x.Id)
            .Select(x => new ServiceRequestListItemResponse
            {
                Id = x.Id,
                Status = x.Status,
                StatusLabel = StatusLabel(x.Status),
                Acknowledged = x.Acknowledged,
                InitiatorName = x.InitiatorName,
                SubmittedAt = x.SubmittedAt,
                ReactionAt = x.ReactionAt,
                CompletedAt = x.CompletedAt,
                DepartmentName = x.ServiceDepartment!.Name,
                Phone = x.Phone,
                BodyPreview = x.Body.Length > 120 ? x.Body.Substring(0, 120) + "…" : x.Body,
                DueAt = x.DueAt
            })
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

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

        return Ok(MapDetail(entity));
    }

    [HttpPost]
    public async Task<ActionResult<ServiceRequestDetailResponse>> Create(
        [FromBody] CreateServiceRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Phone))
        {
            return BadRequest(new { error = "phone_required", message = "Телефон обязателен." });
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return BadRequest(new { error = "body_required", message = "Текст заявки обязателен." });
        }

        if (string.IsNullOrWhiteSpace(request.InitiatorName))
        {
            return BadRequest(new { error = "initiator_required", message = "Инициатор обязателен." });
        }

        var deptExists = await _db.ServiceDepartments
            .AnyAsync(x => x.Id == request.ServiceDepartmentId && x.IsActive, cancellationToken);
        if (!deptExists)
        {
            return BadRequest(new { error = "department_invalid", message = "Служба не найдена." });
        }

        var now = DateTimeOffset.UtcNow;
        var entity = new ServiceRequest
        {
            SubmittedAt = now,
            Phone = request.Phone.Trim(),
            // Npgsql timestamptz принимает только UTC (Offset=0).
            DueAt = request.DueAt.ToUniversalTime(),
            InitiatorName = request.InitiatorName.Trim(),
            ServiceDepartmentId = request.ServiceDepartmentId,
            TransportType = string.IsNullOrWhiteSpace(request.TransportType) ? null : request.TransportType.Trim(),
            Body = request.Body.Trim(),
            Status = ServiceRequestStatus.Submitted,
            CreatedByUserId = GetUserId(),
            LastModifiedAt = now,
            LastModifiedByUserId = GetUserId()
        };

        _db.ServiceRequests.Add(entity);
        await _db.SaveChangesAsync(cancellationToken);

        await _db.Entry(entity).Reference(x => x.ServiceDepartment).LoadAsync(cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, MapDetail(entity));
    }

    [HttpPost("{id:long}/transition")]
    [Authorize(Policy = AuthorizationPolicies.ManageBusinessRecords)]
    public async Task<ActionResult<ServiceRequestDetailResponse>> Transition(
        long id,
        [FromBody] TransitionServiceRequestRequest request,
        CancellationToken cancellationToken)
    {
        var entity = await _db.ServiceRequests
            .Include(x => x.ServiceDepartment)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        if (!string.IsNullOrWhiteSpace(request.RowVersion)
            && uint.TryParse(request.RowVersion, out var expectedXmin)
            && entity.Xmin != expectedXmin)
        {
            return Conflict(new { error = "concurrency", message = "Заявка изменена другим пользователем." });
        }

        var action = (request.Action ?? string.Empty).Trim().ToLowerInvariant();
        var comment = string.IsNullOrWhiteSpace(request.ExecutorComment)
            ? entity.ExecutorComment
            : request.ExecutorComment.Trim();

        switch (action)
        {
            case "accept":
                if (!TryEnsureStatus(entity, out var acceptErr, ServiceRequestStatus.Submitted, ServiceRequestStatus.Returned))
                {
                    return BadRequest(new { error = "invalid_status", message = acceptErr });
                }

                entity.Status = ServiceRequestStatus.InProgress;
                entity.ReactionAt ??= DateTimeOffset.UtcNow;
                break;
            case "reject":
                if (!TryEnsureStatus(entity, out var rejectErr, ServiceRequestStatus.Submitted, ServiceRequestStatus.InProgress, ServiceRequestStatus.Returned))
                {
                    return BadRequest(new { error = "invalid_status", message = rejectErr });
                }

                entity.Status = ServiceRequestStatus.Rejected;
                entity.ReactionAt ??= DateTimeOffset.UtcNow;
                entity.CompletedAt = DateTimeOffset.UtcNow;
                break;
            case "return":
                if (!TryEnsureStatus(entity, out var returnErr, ServiceRequestStatus.Submitted, ServiceRequestStatus.InProgress))
                {
                    return BadRequest(new { error = "invalid_status", message = returnErr });
                }

                entity.Status = ServiceRequestStatus.Returned;
                entity.ReactionAt ??= DateTimeOffset.UtcNow;
                break;
            case "complete":
                if (!TryEnsureStatus(entity, out var completeErr, ServiceRequestStatus.InProgress))
                {
                    return BadRequest(new { error = "invalid_status", message = completeErr });
                }

                entity.Status = ServiceRequestStatus.Completed;
                entity.CompletedAt = DateTimeOffset.UtcNow;
                break;
            case "cancel":
                if (!TryEnsureStatus(entity, out var cancelErr,
                        ServiceRequestStatus.Submitted,
                        ServiceRequestStatus.InProgress,
                        ServiceRequestStatus.Returned))
                {
                    return BadRequest(new { error = "invalid_status", message = cancelErr });
                }

                entity.Status = ServiceRequestStatus.Cancelled;
                entity.CompletedAt = DateTimeOffset.UtcNow;
                break;
            case "reschedule":
                if (!TryEnsureStatus(entity, out var reschedErr, ServiceRequestStatus.InProgress))
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
                entity.Acknowledged = request.Acknowledged ?? true;
                break;
            default:
                return BadRequest(new { error = "action_unknown", message = "Неизвестное действие." });
        }

        entity.ExecutorComment = comment;
        entity.LastModifiedAt = DateTimeOffset.UtcNow;
        entity.LastModifiedByUserId = GetUserId();
        await _db.SaveChangesAsync(cancellationToken);
        return Ok(MapDetail(entity));
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

        var path = _attachments.GetAbsolutePath(attachment.StoragePath);
        if (!global::System.IO.File.Exists(path))
        {
            return NotFound();
        }

        return PhysicalFile(path, attachment.ContentType, attachment.FileName);
    }

    private async Task<ServiceRequest?> LoadAccessibleAsync(long id, CancellationToken cancellationToken)
    {
        var entity = await _db.ServiceRequests
            .Include(x => x.ServiceDepartment)
            .Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (User.IsInRole(AppRoles.Initiator)
            && !User.IsInRole(AppRoles.Dispatcher)
            && !User.IsInRole(AppRoles.Administrator)
            && entity.CreatedByUserId != GetUserId())
        {
            return null;
        }

        return entity;
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

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

    private static ServiceRequestDetailResponse MapDetail(ServiceRequest x) => new()
    {
        Id = x.Id,
        Status = x.Status,
        StatusLabel = StatusLabel(x.Status),
        Acknowledged = x.Acknowledged,
        SubmittedAt = x.SubmittedAt,
        Phone = x.Phone,
        DueAt = x.DueAt,
        InitiatorName = x.InitiatorName,
        ServiceDepartmentId = x.ServiceDepartmentId,
        DepartmentName = x.ServiceDepartment?.Name ?? string.Empty,
        TransportType = x.TransportType,
        Body = x.Body,
        ExecutorComment = x.ExecutorComment,
        ReactionAt = x.ReactionAt,
        CompletedAt = x.CompletedAt,
        RowVersion = x.Xmin.ToString(),
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

    internal static string StatusLabel(ServiceRequestStatus status) => status switch
    {
        ServiceRequestStatus.Submitted => "Заявка подана",
        ServiceRequestStatus.InProgress => "В работе",
        ServiceRequestStatus.Rejected => "Заявка отклонена",
        ServiceRequestStatus.Returned => "Возвращена для уточнения",
        ServiceRequestStatus.Completed => "Заявка выполнена",
        ServiceRequestStatus.Cancelled => "Отменена",
        _ => status.ToString()
    };
}
