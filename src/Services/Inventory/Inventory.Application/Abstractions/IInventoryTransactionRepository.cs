using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Domain.Entities;

namespace InventorySystem.Stock.Application.Abstractions;

public interface IInventoryTransactionRepository
{
    void Add(InventoryTransaction transaction);

    Task<PagedResult<InventoryTransaction>> ListAsync(HistoryQuery query, CancellationToken cancellationToken = default);
}
