using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Stock.Infrastructure.Persistence.Repositories;

public class InventoryTransactionRepository : IInventoryTransactionRepository
{
    private readonly InventoryDbContext _dbContext;

    public InventoryTransactionRepository(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Add(InventoryTransaction transaction) => _dbContext.Transactions.Add(transaction);

    public async Task<PagedResult<InventoryTransaction>> ListAsync(HistoryQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();

        var source = _dbContext.Transactions.AsNoTracking().AsQueryable();

        if (normalized.ProductId is { } productId)
        {
            source = source.Where(transaction => transaction.ProductId == productId);
        }

        if (normalized.WarehouseId is { } warehouseId)
        {
            source = source.Where(transaction => transaction.WarehouseId == warehouseId);
        }

        if (normalized.Type is { } type)
        {
            source = source.Where(transaction => transaction.Type == type);
        }

        if (normalized.From is { } from)
        {
            source = source.Where(transaction => transaction.CreatedAt >= from);
        }

        if (normalized.To is { } to)
        {
            source = source.Where(transaction => transaction.CreatedAt <= to);
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(transaction => transaction.CreatedAt)
            .ThenByDescending(transaction => transaction.Id)
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<InventoryTransaction>(items, normalized.Page, normalized.PageSize, totalCount);
    }
}
