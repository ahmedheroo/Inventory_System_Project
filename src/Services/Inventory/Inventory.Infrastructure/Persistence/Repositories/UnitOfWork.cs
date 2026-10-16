using InventorySystem.Stock.Application.Abstractions;

namespace InventorySystem.Stock.Infrastructure.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly InventoryDbContext _dbContext;

    public UnitOfWork(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
