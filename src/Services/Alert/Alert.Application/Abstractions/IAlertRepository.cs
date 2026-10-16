using InventorySystem.Alerts.Application.Dtos;
using InventorySystem.Alerts.Domain.Entities;

namespace InventorySystem.Alerts.Application.Abstractions;

public interface IAlertRepository
{
    Task<Alert?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The alert protecting a (product, warehouse) pair, if any is still active.
    /// This is what makes duplicate alerts impossible: the consumer updates this
    /// row instead of inserting another one.
    /// </summary>
    Task<Alert?> FindActiveAsync(int productId, int warehouseId, CancellationToken cancellationToken = default);

    Task<PagedResult<Alert>> ListAsync(AlertQuery query, CancellationToken cancellationToken = default);

    void Add(Alert alert);
}

/// <summary>
/// Commits the alerts raised by one event together with that event's
/// de-duplication record.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
