using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Auth;
using Crown.Contracts.CatalogParts;
using Crown.Contracts.Common;
using Crown.Infrastructure.Api;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Persistence;

namespace Crown.Features.CatalogParts;

[ApiController]
[Route("api/catalog-parts")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class CatalogPartsController : ControllerBase
{
    /// <summary>Значение category= для фильтра «без категории».</summary>
    public const string NoCategoryFilterValue = "__none__";

    private readonly AppDbContext _dbContext;

    public CatalogPartsController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<CatalogPartResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] bool activeOnly = false,
        [FromQuery] string? activeFilter = null,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.CatalogParts.AsQueryable();

        var normalizedActiveFilter = (activeFilter ?? string.Empty).Trim().ToLowerInvariant();
        if (normalizedActiveFilter is "active" || (activeOnly && string.IsNullOrEmpty(normalizedActiveFilter)))
        {
            query = query.Where(x => x.IsActive);
        }
        else if (normalizedActiveFilter is "inactive")
        {
            query = query.Where(x => !x.IsActive);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x =>
                x.Name.ToLower().Contains(normalizedSearch)
                || (x.Code != null && x.Code.ToLower().Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            var normalizedCategory = category.Trim();
            if (string.Equals(normalizedCategory, NoCategoryFilterValue, StringComparison.Ordinal))
            {
                query = query.Where(x => x.Category == null || x.Category == string.Empty);
            }
            else
            {
                var categoryLower = normalizedCategory.ToLower();
                query = query.Where(x => x.Category != null && x.Category.ToLower() == categoryLower);
            }
        }

        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);

        var response = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => MapResponse(x))
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetCategories(CancellationToken cancellationToken)
    {
        var categories = await _dbContext.CatalogParts
            .Where(x => x.Category != null && x.Category != string.Empty)
            .Select(x => x.Category!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CatalogPartResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await _dbContext.CatalogParts
            .Where(x => x.Id == id)
            .Select(x => MapResponse(x))
            .FirstOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManagePartsCatalog)]
    public async Task<ActionResult<CatalogPartResponse>> Create(
        SaveCatalogPartRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);

        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        var entity = new CatalogPart
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Code = NormalizeOptional(request.Code),
            Category = NormalizeOptional(request.Category),
            DefaultUnitPrice = request.DefaultUnitPrice,
            DefaultUnit = request.DefaultUnit.Trim(),
            Description = NormalizeOptional(request.Description),
            IsActive = request.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.CatalogParts.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, MapResponse(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManagePartsCatalog)]
    public async Task<ActionResult<CatalogPartResponse>> Update(
        Guid id,
        SaveCatalogPartRequest request,
        CancellationToken cancellationToken)
    {
        var validationError = ValidateRequest(request);

        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        var entity = await _dbContext.CatalogParts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        entity.Name = request.Name.Trim();
        entity.Code = NormalizeOptional(request.Code);
        entity.Category = NormalizeOptional(request.Category);
        entity.DefaultUnitPrice = request.DefaultUnitPrice;
        entity.DefaultUnit = request.DefaultUnit.Trim();
        entity.Description = NormalizeOptional(request.Description);
        entity.IsActive = request.IsActive;

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapResponse(entity));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManagePartsCatalog)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        var entity = await _dbContext.CatalogParts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        _dbContext.CatalogParts.Remove(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    private static string? ValidateRequest(SaveCatalogPartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiErrorCodes.ServiceNameRequired;
        }

        if (string.IsNullOrWhiteSpace(request.DefaultUnit))
        {
            return ApiErrorCodes.DefaultUnitRequired;
        }

        if (!MeasurementUnits.IsAllowed(request.DefaultUnit))
        {
            return ApiErrorCodes.UnsupportedMeasurementUnit;
        }

        if (request.DefaultUnitPrice < 0)
        {
            return ApiErrorCodes.DefaultUnitPriceNegative;
        }

        return null;
    }

    private static string? NormalizeOptional(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static CatalogPartResponse MapResponse(CatalogPart entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Code = entity.Code,
            Category = entity.Category,
            DefaultUnitPrice = entity.DefaultUnitPrice,
            DefaultUnit = entity.DefaultUnit,
            Description = entity.Description,
            IsActive = entity.IsActive,
            CreatedAtUtc = entity.CreatedAtUtc
        };
}
