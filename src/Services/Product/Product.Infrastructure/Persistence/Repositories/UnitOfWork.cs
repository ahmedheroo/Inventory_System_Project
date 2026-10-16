using InventorySystem.Products.Application.Abstractions;

namespace InventorySystem.Products.Infrastructure.Persistence.Repositories;

public class UnitOfWork : IUnitOfWork
{
    private readonly ProductDbContext _dbContext;

    public UnitOfWork(ProductDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
