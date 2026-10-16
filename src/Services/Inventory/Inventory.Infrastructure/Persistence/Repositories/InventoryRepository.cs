using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Stock.Infrastructure.Persistence.Repositories;

public class InventoryRepository : IInventoryRepository
{
    private readonly InventoryDbContext _dbContext;

    public InventoryRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Inventory?> GetAsync(int productId, int warehouseId, CancellationToken cancellationToken = default) =>
        _dbContext.StockLevels.FirstOrDefaultAsync(
            inventory => inventory.ProductId == productId && inventory.WarehouseId == warehouseId,
            cancellationToken);

    public Task<Inventory?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _dbContext.StockLevels.FirstOrDefaultAsync(inventory => inventory.Id == id, cancellationToken);

    public async Task<PagedResult<Inventory>> ListAsync(InventoryQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();

        var source = _dbContext.StockLevels.AsNoTracking().AsQueryable();

        if (normalized.ProductId is { } productId)
        {
            source = source.Where(inventory => inventory.ProductId == productId);
        }

        if (normalized.WarehouseId is { } warehouseId)
        {
            source = source.Where(inventory => inventory.WarehouseId == warehouseId);
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(inventory => inventory.ProductId)
            .ThenBy(inventory => inventory.WarehouseId)
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Inventory>(items, normalized.Page, normalized.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<Inventory>> ListByProductAsync(int productId, CancellationToken cancellationToken = default) =>
        await _dbContext.StockLevels
            .AsNoTracking()
            .Where(inventory => inventory.ProductId == productId)
            .OrderBy(inventory => inventory.WarehouseId)
            .ToListAsync(cancellationToken);

    public void Add(Inventory inventory) => _dbContext.StockLevels.Add(inventory);
}
