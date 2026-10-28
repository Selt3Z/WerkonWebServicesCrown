using Microsoft.EntityFrameworkCore;
using Crown.Contracts.Common;
using Crown.Domain.Entities;
using Crown.Infrastructure.Persistence;
using Crown.Infrastructure.Settings;

namespace Crown.Infrastructure.Warehouse;

public sealed class WarehouseStockService
{
    private readonly AppDbContext _dbContext;
    private readonly AppSettingsService _appSettingsService;

    public WarehouseStockService(AppDbContext dbContext, AppSettingsService appSettingsService)
    {
        _dbContext = dbContext;
        _appSettingsService = appSettingsService;
    }

    public async Task<string?> TryDeductAsync(
        WarehousePart warehousePart,
        decimal quantity,
        CancellationToken cancellationToken,
        Guid? serviceRequestId = null,
        Guid? userId = null,
        string? userDisplayName = null)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return ApiErrorCodes.WarehouseDisabled;
        }

        if (string.IsNullOrWhiteSpace(warehousePart.Code))
        {
            return ApiErrorCodes.WarehousePartCodeRequired;
        }

        if (quantity <= 0)
        {
            return ApiErrorCodes.InsufficientWarehouseStock;
        }

        if (warehousePart.QuantityOnHand < quantity)
        {
            return ApiErrorCodes.InsufficientWarehouseStock;
        }

        warehousePart.QuantityOnHand -= quantity;
        AddMovement(
            warehousePart,
            quantityDelta: -quantity,
            kind: WarehouseStockMovementKind.Deduction,
            serviceRequestId: serviceRequestId,
            userId: userId,
            userDisplayName: userDisplayName);
        return null;
    }

    /// <summary>Ручная корректировка остатка с записью Adjustment-движения.</summary>
    public async Task<string?> AdjustQuantityAsync(
        WarehousePart warehousePart,
        decimal quantityDelta,
        CancellationToken cancellationToken,
        string? reason = null,
        Guid? userId = null,
        string? userDisplayName = null)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return ApiErrorCodes.WarehouseDisabled;
        }

        if (quantityDelta == 0)
        {
            return null;
        }

        var newQuantity = warehousePart.QuantityOnHand + quantityDelta;
        if (newQuantity < 0)
        {
            return ApiErrorCodes.InsufficientWarehouseStock;
        }

        warehousePart.QuantityOnHand = newQuantity;
        AddMovement(
            warehousePart,
            quantityDelta,
            WarehouseStockMovementKind.Adjustment,
            reason: reason,
            userId: userId,
            userDisplayName: userDisplayName);
        return null;
    }

    /// <summary>Приход при создании карточки с начальным остатком &gt; 0.</summary>
    public void RecordReceipt(
        WarehousePart warehousePart,
        decimal quantity,
        Guid? userId = null,
        string? userDisplayName = null)
    {
        if (quantity <= 0)
        {
            return;
        }

        AddMovement(
            warehousePart,
            quantityDelta: quantity,
            kind: WarehouseStockMovementKind.Receipt,
            userId: userId,
            userDisplayName: userDisplayName);
    }

    /// <summary>
    /// Возвращает количество на склад; true — остаток реально увеличен.
    /// stockDeducted — флаг позиции заявки «остаток был списан» (вызывающий сам сбрасывает его при true).
    /// </summary>
    public async Task<bool> TryReturnAsync(
        bool stockDeducted,
        Guid? warehousePartId,
        string? partCode,
        string name,
        decimal quantity,
        Guid? serviceRequestId,
        CancellationToken cancellationToken)
    {
        if (!await _appSettingsService.GetWarehouseEnabledAsync(cancellationToken))
        {
            return false;
        }

        if (!stockDeducted || quantity <= 0)
        {
            return false;
        }

        // Архивные тоже ищем — возврат не должен теряться после soft-archive.
        WarehousePart? warehousePart = null;
        if (warehousePartId.HasValue)
        {
            warehousePart = await _dbContext.WarehouseParts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.Id == warehousePartId.Value, cancellationToken);
        }

        // FirstOrDefault, не Single: дубликаты Code+Name раньше валили возврат.
        if (warehousePart is null && !string.IsNullOrWhiteSpace(partCode))
        {
            warehousePart = await _dbContext.WarehouseParts
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    x => x.Code == partCode && x.Name == name,
                    cancellationToken);
        }

        if (warehousePart is null)
        {
            return false;
        }

        warehousePart.QuantityOnHand += quantity;
        AddMovement(
            warehousePart,
            quantityDelta: quantity,
            kind: WarehouseStockMovementKind.Return,
            serviceRequestId: serviceRequestId);
        return true;
    }

    private void AddMovement(
        WarehousePart warehousePart,
        decimal quantityDelta,
        WarehouseStockMovementKind kind,
        string? reason = null,
        Guid? serviceRequestId = null,
        Guid? userId = null,
        string? userDisplayName = null)
    {
        _dbContext.WarehouseStockMovements.Add(new WarehouseStockMovement
        {
            Id = Guid.NewGuid(),
            WarehousePartId = warehousePart.Id,
            OccurredAtUtc = DateTime.UtcNow,
            QuantityDelta = quantityDelta,
            QuantityAfter = warehousePart.QuantityOnHand,
            Kind = kind,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ServiceRequestId = serviceRequestId,
            UserId = userId,
            UserDisplayName = string.IsNullOrWhiteSpace(userDisplayName) ? null : userDisplayName.Trim()
        });
    }
}
