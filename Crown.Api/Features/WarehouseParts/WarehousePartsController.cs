using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Contracts.WarehouseParts;
using Crown.Domain;
using Crown.Domain.Entities;
using Crown.Infrastructure.Api;
using Crown.Infrastructure.Identity;
using Crown.Infrastructure.Pdf;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;
using Crown.Infrastructure.Time;
using Crown.Infrastructure.Warehouse;

namespace Crown.Features.WarehouseParts;

[ApiController]
[Route("api/warehouse-parts")]
[Authorize(Policy = AuthorizationPolicies.BusinessData)]
public sealed class WarehousePartsController : ControllerBase
{
    private const int MaxPdfRows = 5_000;

    private readonly AppDbContext _dbContext;
    private readonly AppSettingsService _appSettingsService;
    private readonly WarehouseStockService _warehouseStockService;
    private readonly WarehouseInventoryPdfGenerator _inventoryPdfGenerator;
    private readonly AppTimeZone _appTimeZone;

    public WarehousePartsController(
        AppDbContext dbContext,
        AppSettingsService appSettingsService,
        WarehouseStockService warehouseStockService,
        WarehouseInventoryPdfGenerator inventoryPdfGenerator,
        AppTimeZone appTimeZone)
    {
        _dbContext = dbContext;
        _appSettingsService = appSettingsService;
        _warehouseStockService = warehouseStockService;
        _inventoryPdfGenerator = inventoryPdfGenerator;
        _appTimeZone = appTimeZone;
    }

    /// <summary>Значение category= для фильтра «без категории».</summary>
    public const string NoCategoryFilterValue = "__none__";

    [HttpGet]
    public async Task<ActionResult<PagedResponse<WarehousePartResponse>>> GetAll(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? code,
        [FromQuery] bool inStockOnly = false,
        [FromQuery] bool shortageOnly = false,
        [FromQuery] bool includeArchived = false,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return NotFound();
        }

        var query = BuildListQuery(search, category, code, inStockOnly, shortageOnly, includeArchived);
        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);

        var response = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => MapResponse(x))
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

    [HttpGet("pdf")]
    public async Task<IActionResult> GetInventoryPdf(
        [FromQuery] string? search,
        [FromQuery] string? category,
        [FromQuery] string? code,
        [FromQuery] bool inStockOnly = false,
        [FromQuery] bool shortageOnly = false,
        [FromQuery] bool includeArchived = false,
        [FromQuery] string? culture = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return NotFound();
        }

        var query = BuildListQuery(search, category, code, inStockOnly, shortageOnly, includeArchived);

        var parts = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Take(MaxPdfRows)
            .ToListAsync(cancellationToken);

        var organization = await _appSettingsService.GetOrganizationDocumentInfoAsync(cancellationToken);
        var currencyCode = await _appSettingsService.GetCurrencyCodeAsync(cancellationToken);
        var locale = PdfDocumentLocale.Create(culture, currencyCode);

        var filterParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(search))
        {
            filterParts.Add($"{locale.T("Pdf_ItemName")}: {search.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            filterParts.Add($"{locale.T("Inventory_Category")}: {category.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            filterParts.Add($"{locale.T("Inventory_Code")}: {code.Trim()}");
        }

        if (shortageOnly)
        {
            filterParts.Add(locale.T("Inventory_ShortageOnly"));
        }

        if (includeArchived)
        {
            filterParts.Add(locale.T("Inventory_IncludeArchived"));
        }

        var pdf = _inventoryPdfGenerator.Generate(new WarehouseInventoryPdfData
        {
            Organization = organization,
            Locale = locale,
            GeneratedAtLocal = _appTimeZone.FromUtc(DateTime.UtcNow),
            FilterCaption = filterParts.Count == 0 ? null : string.Join(" · ", filterParts),
            Rows = parts.Select(x => new WarehouseInventoryPdfRow
            {
                Name = x.Name,
                Code = x.Code,
                Barcode = x.Barcode,
                Category = x.Category,
                Unit = x.DefaultUnit,
                QuantityOnHand = x.QuantityOnHand,
                MinQuantityOnHand = x.MinQuantityOnHand,
                UnitPrice = x.DefaultUnitPrice,
                IsArchived = x.IsArchived
            }).ToList()
        });

        return File(pdf, "application/pdf", $"warehouse-inventory-{DateTime.UtcNow:yyyy-MM-dd}.pdf");
    }

    [HttpGet("categories")]
    public async Task<ActionResult<IReadOnlyList<string>>> GetCategories(CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return NotFound();
        }

        var categories = await _dbContext.WarehouseParts
            .Where(x => x.Category != null && x.Category != string.Empty)
            .Select(x => x.Category!)
            .Distinct()
            .OrderBy(x => x)
            .ToListAsync(cancellationToken);

        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<WarehousePartResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return NotFound();
        }

        var response = await _dbContext.WarehouseParts
            .IgnoreQueryFilters()
            .Where(x => x.Id == id)
            .Select(x => MapResponse(x))
            .FirstOrDefaultAsync(cancellationToken);

        if (response is null)
        {
            return NotFound();
        }

        return Ok(response);
    }

    [HttpGet("{id:guid}/movements")]
    public async Task<ActionResult<PagedResponse<WarehouseStockMovementResponse>>> GetMovements(
        Guid id,
        [FromQuery] int? skip = null,
        [FromQuery] int? take = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return NotFound();
        }

        var partExists = await _dbContext.WarehouseParts
            .IgnoreQueryFilters()
            .AnyAsync(x => x.Id == id, cancellationToken);

        if (!partExists)
        {
            return NotFound();
        }

        var (normalizedSkip, normalizedTake) = QueryPagingExtensions.NormalizePaging(skip, take);

        var response = await _dbContext.WarehouseStockMovements
            .Where(x => x.WarehousePartId == id)
            .OrderByDescending(x => x.OccurredAtUtc)
            .ThenByDescending(x => x.Id)
            .Select(x => new WarehouseStockMovementResponse
            {
                Id = x.Id,
                WarehousePartId = x.WarehousePartId,
                OccurredAtUtc = x.OccurredAtUtc,
                QuantityDelta = x.QuantityDelta,
                QuantityAfter = x.QuantityAfter,
                Kind = x.Kind,
                Reason = x.Reason,
                ServiceRequestId = x.ServiceRequestId,
                UserId = x.UserId,
                UserDisplayName = x.UserDisplayName
            })
            .ToPagedResponseAsync(normalizedSkip, normalizedTake, cancellationToken);

        return Ok(response);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<ActionResult<WarehousePartResponse>> Create(
        SaveWarehousePartRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.WarehouseDisabled));
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        var unit = request.DefaultUnit.Trim();
        var quantityOnHand = MeasurementUnits.NormalizeQuantity(unit, request.QuantityOnHand);

        var entity = new WarehousePart
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Code = request.Code!.Trim(),
            Barcode = NormalizeOptional(request.Barcode),
            Category = NormalizeOptional(request.Category),
            DefaultUnitPrice = request.DefaultUnitPrice,
            DefaultUnit = unit,
            QuantityOnHand = quantityOnHand,
            MinQuantityOnHand = MeasurementUnits.NormalizeQuantity(unit, request.MinQuantityOnHand),
            Description = NormalizeOptional(request.Description),
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.WarehouseParts.Add(entity);

        var (userId, userDisplayName) = await ResolveCurrentUserAsync(cancellationToken);
        if (quantityOnHand > 0)
        {
            _warehouseStockService.RecordReceipt(entity, quantityOnHand, userId, userDisplayName);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, MapResponse(entity));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<ActionResult<WarehousePartResponse>> Update(
        Guid id,
        SaveWarehousePartRequest request,
        CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.WarehouseDisabled));
        }

        var validationError = ValidateRequest(request);
        if (validationError is not null)
        {
            return BadRequest(ApiErrors.FromCode(validationError));
        }

        var entity = await _dbContext.WarehouseParts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        var unit = request.DefaultUnit.Trim();
        var newQuantity = MeasurementUnits.NormalizeQuantity(unit, request.QuantityOnHand);
        var quantityDelta = newQuantity - entity.QuantityOnHand;

        entity.Name = request.Name.Trim();
        entity.Code = request.Code!.Trim();
        entity.Barcode = NormalizeOptional(request.Barcode);
        entity.Category = NormalizeOptional(request.Category);
        entity.DefaultUnitPrice = request.DefaultUnitPrice;
        entity.DefaultUnit = unit;
        entity.MinQuantityOnHand = MeasurementUnits.NormalizeQuantity(unit, request.MinQuantityOnHand);
        entity.Description = NormalizeOptional(request.Description);

        if (quantityDelta != 0)
        {
            var (userId, userDisplayName) = await ResolveCurrentUserAsync(cancellationToken);
            var adjustError = await _warehouseStockService.AdjustQuantityAsync(
                entity,
                quantityDelta,
                cancellationToken,
                reason: "manual_update",
                userId: userId,
                userDisplayName: userDisplayName);

            if (adjustError is not null)
            {
                return BadRequest(ApiErrors.FromCode(adjustError));
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(MapResponse(entity));
    }

    /// <summary>
    /// Безвозвратное удаление только архивной запчасти вместе с историей движений.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.WarehouseDisabled));
        }

        var entity = await _dbContext.WarehouseParts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        if (!entity.IsArchived)
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.WarehousePartNotArchived));
        }

        var movements = await _dbContext.WarehouseStockMovements
            .Where(x => x.WarehousePartId == id)
            .ToListAsync(cancellationToken);
        _dbContext.WarehouseStockMovements.RemoveRange(movements);
        _dbContext.WarehouseParts.Remove(entity);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:guid}/archive")]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken cancellationToken) =>
        await SetArchivedAsync(id, archived: true, cancellationToken);

    [HttpPatch("{id:guid}/unarchive")]
    [Authorize(Policy = AuthorizationPolicies.ManageWarehouse)]
    public async Task<IActionResult> Unarchive(Guid id, CancellationToken cancellationToken) =>
        await SetArchivedAsync(id, archived: false, cancellationToken);

    private async Task<IActionResult> SetArchivedAsync(
        Guid id,
        bool archived,
        CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return BadRequest(ApiErrors.FromCode(ApiErrorCodes.WarehouseDisabled));
        }

        var entity = await _dbContext.WarehouseParts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (entity is null)
        {
            return NotFound();
        }

        // Soft-archive: PartCode на строках визитов не трогаем — только прячем из пикеров.
        if (entity.IsArchived != archived)
        {
            entity.IsArchived = archived;
            entity.ArchivedAtUtc = archived ? DateTime.UtcNow : null;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return NoContent();
    }

    private async Task<(Guid? UserId, string? UserDisplayName)> ResolveCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            return (null, User.Identity?.Name);
        }

        var displayName = await _dbContext.Users
            .Where(x => x.Id == userId)
            .Select(x => string.IsNullOrWhiteSpace(x.DisplayName) ? x.UserName : x.DisplayName)
            .FirstOrDefaultAsync(cancellationToken);

        return (userId, displayName ?? User.Identity?.Name);
    }

    private IQueryable<WarehousePart> BuildListQuery(
        string? search,
        string? category,
        string? code,
        bool inStockOnly,
        bool shortageOnly,
        bool includeArchived)
    {
        var query = includeArchived
            ? _dbContext.WarehouseParts.IgnoreQueryFilters()
            : _dbContext.WarehouseParts.AsQueryable();

        if (inStockOnly)
        {
            query = query.Where(x => x.QuantityOnHand > 0 && x.Code != null && x.Code != string.Empty);
        }

        if (shortageOnly)
        {
            query = query.Where(x => x.QuantityOnHand <= x.MinQuantityOnHand);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(x =>
                x.Name.ToLower().Contains(normalizedSearch)
                || (x.Code != null && x.Code.ToLower().Contains(normalizedSearch))
                || (x.Barcode != null && x.Barcode.ToLower().Contains(normalizedSearch)));
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

        if (!string.IsNullOrWhiteSpace(code))
        {
            // Exact lookup для сканера — Code или Barcode.
            var normalizedCode = code.Trim().ToLower();
            query = query.Where(x =>
                (x.Code != null && x.Code.ToLower() == normalizedCode)
                || (x.Barcode != null && x.Barcode.ToLower() == normalizedCode));
        }

        return query;
    }

    private static string? ValidateRequest(SaveWarehousePartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiErrorCodes.PartNameRequired;
        }

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return ApiErrorCodes.WarehousePartCodeRequired;
        }

        if (string.IsNullOrWhiteSpace(request.DefaultUnit))
        {
            return ApiErrorCodes.DefaultUnitRequired;
        }

        if (!MeasurementUnits.IsAllowed(request.DefaultUnit))
        {
            return ApiErrorCodes.UnsupportedMeasurementUnit;
        }

        if (!MeasurementUnits.IsValidQuantity(request.DefaultUnit.Trim(), request.QuantityOnHand))
        {
            return ApiErrorCodes.InvalidQuantityForUnit;
        }

        if (request.DefaultUnitPrice < 0)
        {
            return ApiErrorCodes.DefaultUnitPriceNegative;
        }

        if (request.QuantityOnHand < 0)
        {
            return ApiErrorCodes.QuantityOnHandNegative;
        }

        if (request.MinQuantityOnHand < 0)
        {
            return ApiErrorCodes.QuantityOnHandNegative;
        }

        if (!MeasurementUnits.IsValidQuantity(request.DefaultUnit.Trim(), request.MinQuantityOnHand))
        {
            return ApiErrorCodes.InvalidQuantityForUnit;
        }

        return null;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static WarehousePartResponse MapResponse(WarehousePart entity) =>
        new()
        {
            Id = entity.Id,
            Name = entity.Name,
            Code = entity.Code,
            Barcode = entity.Barcode,
            Category = entity.Category,
            DefaultUnitPrice = entity.DefaultUnitPrice,
            DefaultUnit = entity.DefaultUnit,
            QuantityOnHand = entity.QuantityOnHand,
            MinQuantityOnHand = entity.MinQuantityOnHand,
            Description = entity.Description,
            IsArchived = entity.IsArchived,
            ArchivedAtUtc = entity.ArchivedAtUtc,
            CreatedAtUtc = entity.CreatedAtUtc
        };
}
