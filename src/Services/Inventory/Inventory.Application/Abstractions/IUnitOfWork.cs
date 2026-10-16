namespace InventorySystem.Stock.Application.Abstractions;

/// <summary>
/// Commits the stock change and its ledger transaction(s) atomically: a
/// transfer, for example, writes two inventory rows and two transactions in a
/// single <c>SaveChanges</c>.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
