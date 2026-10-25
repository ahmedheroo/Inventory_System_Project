using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Reports.Application.Abstractions;
using InventorySystem.Reports.Application.Dtos;
using InventorySystem.Reports.Application.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Reports.Infrastructure.Persistence.Repositories;

public class ReportingRepository : IReportingRepository
{
    private readonly ReportingDbContext _dbContext;

    public ReportingRepository(ReportingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<StockLevelSnapshot>> ListStockLevelsAsync(
        StockLevelFilter filter,
        CancellationToken cancellationToken = default)
    {
        var source = _dbContext.StockLevels.AsNoTracking().AsQueryable();

        if (filter.ProductId is { } productId)
        {
            source = source.Where(stockLevel => stockLevel.ProductId == productId);
        }

        if (filter.WarehouseId is { } warehouseId)
        {
            source = source.Where(stockLevel => stockLevel.WarehouseId == warehouseId);
        }

        return await source
            .OrderBy(stockLevel => stockLevel.ProductId)
            .ThenBy(stockLevel => stockLevel.WarehouseId)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<StockMovement>> ListMovementsAsync(
        MovementFilter filter,
        CancellationToken cancellationToken = default)
    {
        var source = FilteredMovements(filter);

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(movement => movement.OccurredAt)
            .ThenByDescending(movement => movement.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<StockMovement>(items, filter.Page, filter.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<StockMovement>> ListAllMovementsAsync(
        MovementFilter filter,
        CancellationToken cancellationToken = default) =>
        await FilteredMovements(filter)
            .OrderByDescending(movement => movement.OccurredAt)
            .ThenByDescending(movement => movement.Id)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Aggregated by the database so the ledger is never loaded to build a
    /// summary. Magnitude sums are computed per movement type and combined here,
    /// because only the type says which direction a quantity went.
    /// </summary>
    public async Task<MovementTotals> SumMovementsAsync(
        MovementFilter filter,
        CancellationToken cancellationToken = default)
    {
        var byType = await FilteredMovements(filter)
            .GroupBy(movement => movement.Type)
            .Select(group => new
            {
                Type = group.Key,
                Count = group.Count(),
                Quantity = group.Sum(movement => movement.Quantity),
                Increase = group.Sum(movement => movement.Quantity > 0 ? movement.Quantity : 0),
                Decrease = group.Sum(movement => movement.Quantity < 0 ? -movement.Quantity : 0)
            })
            .ToListAsync(cancellationToken);

        int Sum(StockTransactionType type) =>
            byType.FirstOrDefault(row => row.Type == type)?.Quantity ?? 0;

        int Increase(StockTransactionType type) =>
            byType.FirstOrDefault(row => row.Type == type)?.Increase ?? 0;

        int Decrease(StockTransactionType type) =>
            byType.FirstOrDefault(row => row.Type == type)?.Decrease ?? 0;

        var received = Sum(StockTransactionType.RECEIVE);
        var issued = Sum(StockTransactionType.ISSUE);
        var transferIn = Sum(StockTransactionType.TRANSFER_IN);
        var transferOut = Sum(StockTransactionType.TRANSFER_OUT);
        var adjusted = Sum(StockTransactionType.ADJUSTMENT);

        return new MovementTotals(
            byType.Sum(row => row.Count),
            received,
            issued,
            transferIn + transferOut,
            Increase(StockTransactionType.ADJUSTMENT),
            Decrease(StockTransactionType.ADJUSTMENT),
            received + transferIn + adjusted - issued - transferOut);
    }

    public Task<StockLevelSnapshot?> FindStockLevelAsync(
        int productId,
        int warehouseId,
        CancellationToken cancellationToken = default) =>
        _dbContext.StockLevels.FirstOrDefaultAsync(
            stockLevel => stockLevel.ProductId == productId && stockLevel.WarehouseId == warehouseId,
            cancellationToken);

    public void AddStockLevel(StockLevelSnapshot stockLevel) => _dbContext.StockLevels.Add(stockLevel);

    public void AddMovement(StockMovement movement) => _dbContext.StockMovements.Add(movement);

    private IQueryable<StockMovement> FilteredMovements(MovementFilter filter)
    {
        var source = _dbContext.StockMovements.AsNoTracking().AsQueryable();

        if (filter.ProductId is { } productId)
        {
            source = source.Where(movement => movement.ProductId == productId);
        }

        if (filter.WarehouseId is { } warehouseId)
        {
            source = source.Where(movement => movement.WarehouseId == warehouseId);
        }

        if (filter.Type is { } type)
        {
            source = source.Where(movement => movement.Type == type);
        }

        if (filter.From is { } from)
        {
            source = source.Where(movement => movement.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            source = source.Where(movement => movement.OccurredAt <= to);
        }

        return source;
    }
}
