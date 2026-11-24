using System.Security.Claims;
using Crown.Contracts.Feedback;
using Crown.Domain.Entities;
using Crown.Infrastructure.Attachments;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Crown.Features.Feedback;

[ApiController]
[Route("api/feedback")]
[Authorize]
public sealed class FeedbackController(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    AttachmentStorageService attachments) : ControllerBase
{
    private const int MaxBodyLength = 8000;
    private const int MaxSectionKeyLength = 64;
    private const int MaxSectionLabelLength = 120;

    [HttpPost]
    public async Task<ActionResult<FeedbackDetailResponse>> Create(
        [FromBody] CreateFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var body = (request.Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            return BadRequest(new { error = "body_required", message = "Укажите текст сообщения." });
        }

        if (body.Length > MaxBodyLength)
        {
            return BadRequest(new { error = "body_too_long", message = $"Текст длиннее {MaxBodyLength} символов." });
        }

        var user = await users.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId.Value, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        var sectionKey = NormalizeOptional(request.SectionKey, MaxSectionKeyLength);
        var sectionLabel = NormalizeOptional(request.SectionLabel, MaxSectionLabelLength);
        if (sectionKey is null)
        {
            sectionLabel = null;
        }

        var entity = new FeedbackReport
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = userId.Value,
            DisplayName = UserDisplay.Format(user),
            SectionKey = sectionKey,
            SectionLabel = sectionLabel,
            Body = body
        };
        db.FeedbackReports.Add(entity);
        await db.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, MapDetail(entity));
    }

    [HttpPost("{id:guid}/attachments")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<FeedbackAttachmentResponse>> UploadAttachment(
        Guid id,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        if (userId is null)
        {
            return Unauthorized();
        }

        var report = await db.FeedbackReports
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (report is null)
        {
            return NotFound();
        }

        // Вложения только автор или админ.
        if (report.UserId != userId.Value && !User.IsInRole(AppRoles.Administrator))
        {
            return Forbid();
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "file_required", message = "Файл не передан." });
        }

        if (file.Length > attachments.MaxFileBytes)
        {
            return BadRequest(new { error = "file_too_large", message = "Файл слишком большой." });
        }

        await using var stream = file.OpenReadStream();
        var (relative, size) = await attachments.SaveFeedbackAsync(
            id, file.FileName, stream, cancellationToken);

        var attachment = new FeedbackAttachment
        {
            Id = Guid.NewGuid(),
            FeedbackReportId = id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            SizeBytes = size,
            StoragePath = relative,
            UploadedAt = DateTimeOffset.UtcNow,
            UploadedByUserId = userId
        };
        db.FeedbackAttachments.Add(attachment);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new FeedbackAttachmentResponse
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes
        });
    }

    [HttpGet]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<List<FeedbackListItemResponse>>> List(
        [FromQuery] string? q = null,
        CancellationToken cancellationToken = default)
    {
        var query = db.FeedbackReports.AsNoTracking().AsQueryable();
        var term = (q ?? string.Empty).Trim();
        if (term.Length > 0)
        {
            var like = $"%{term}%";
            query = query.Where(x =>
                EF.Functions.ILike(x.DisplayName, like)
                || EF.Functions.ILike(x.Body, like)
                || (x.SectionLabel != null && EF.Functions.ILike(x.SectionLabel, like)));
        }

        var rows = await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new FeedbackListItemResponse
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                UserId = x.UserId,
                DisplayName = x.DisplayName,
                SectionKey = x.SectionKey,
                SectionLabel = x.SectionLabel,
                BodyPreview = x.Body.Length > 160 ? x.Body.Substring(0, 160) + "…" : x.Body,
                AttachmentCount = x.Attachments.Count
            })
            .Take(200)
            .ToListAsync(cancellationToken);

        return Ok(rows);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<ActionResult<FeedbackDetailResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var entity = await db.FeedbackReports.AsNoTracking()
            .Include(x => x.Attachments)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null)
        {
            return NotFound();
        }

        return Ok(MapDetail(entity));
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    [Authorize(Roles = AppRoles.Administrator)]
    public async Task<IActionResult> DownloadAttachment(
        Guid id,
        Guid attachmentId,
        CancellationToken cancellationToken)
    {
        var attachment = await db.FeedbackAttachments.AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == attachmentId && x.FeedbackReportId == id,
                cancellationToken);
        if (attachment is null)
        {
            return NotFound();
        }

        var path = attachments.ResolveExistingPath(attachment.StoragePath);
        if (path is null)
        {
            return NotFound();
        }

        return PhysicalFile(path, attachment.ContentType, attachment.FileName);
    }

    private static FeedbackDetailResponse MapDetail(FeedbackReport entity) => new()
    {
        Id = entity.Id,
        CreatedAt = entity.CreatedAt,
        UserId = entity.UserId,
        DisplayName = entity.DisplayName,
        SectionKey = entity.SectionKey,
        SectionLabel = entity.SectionLabel,
        Body = entity.Body,
        Attachments = entity.Attachments
            .OrderBy(a => a.UploadedAt)
            .Select(a => new FeedbackAttachmentResponse
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                SizeBytes = a.SizeBytes
            })
            .ToList()
    };

    private static string? NormalizeOptional(string? value, int maxLen)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return trimmed.Length <= maxLen ? trimmed : trimmed[..maxLen];
    }

    private Guid? GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
