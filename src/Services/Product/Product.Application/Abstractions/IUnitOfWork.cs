namespace InventorySystem.Products.Application.Abstractions;

/// Commits all pending repository changes in a single transaction boundary.
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
