using InventorySystem.Products.Application.Abstractions;
using InventorySystem.Products.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Products.Infrastructure.Persistence.Repositories;

public class WarehouseRepository : IWarehouseRepository
{
    private readonly ProductDbContext _dbContext;

    public WarehouseRepository(ProductDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Warehouse?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _dbContext.Warehouses.FirstOrDefaultAsync(warehouse => warehouse.Id == id, cancellationToken);

    public Task<Warehouse?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = Warehouse.NormalizeCode(code);
        return _dbContext.Warehouses.FirstOrDefaultAsync(warehouse => warehouse.Code == normalized, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, int? excludeWarehouseId = null, CancellationToken cancellationToken = default)
    {
        var normalized = Warehouse.NormalizeCode(code);

        return _dbContext.Warehouses.AnyAsync(
            warehouse => warehouse.Code == normalized
                         && (excludeWarehouseId == null || warehouse.Id != excludeWarehouseId),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Warehouse>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var source = _dbContext.Warehouses.AsNoTracking().AsQueryable();

        if (!includeInactive)
        {
            source = source.Where(warehouse => warehouse.IsActive);
        }

        return await source
            .OrderBy(warehouse => warehouse.Name)
            .ToListAsync(cancellationToken);
    }

    public void Add(Warehouse warehouse) => _dbContext.Warehouses.Add(warehouse);

    public void Update(Warehouse warehouse) => _dbContext.Warehouses.Update(warehouse);
}
