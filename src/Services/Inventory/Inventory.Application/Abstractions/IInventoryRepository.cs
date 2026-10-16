using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Domain.Entities;

namespace InventorySystem.Stock.Application.Abstractions;

public interface IInventoryRepository
{
    Task<Inventory?> GetAsync(int productId, int warehouseId, CancellationToken cancellationToken = default);

    Task<Inventory?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<PagedResult<Inventory>> ListAsync(InventoryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Inventory>> ListByProductAsync(int productId, CancellationToken cancellationToken = default);

    void Add(Inventory inventory);
}
