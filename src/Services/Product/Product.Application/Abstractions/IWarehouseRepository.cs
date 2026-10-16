using InventorySystem.Products.Domain.Entities;

namespace InventorySystem.Products.Application.Abstractions;

public interface IWarehouseRepository
{
    Task<Warehouse?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Warehouse?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, int? excludeWarehouseId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Warehouse>> ListAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    void Add(Warehouse warehouse);

    void Update(Warehouse warehouse);
}
