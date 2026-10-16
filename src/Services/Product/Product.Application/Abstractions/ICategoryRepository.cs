using InventorySystem.Products.Domain.Entities;

namespace InventorySystem.Products.Application.Abstractions;

public interface ICategoryRepository
{
    Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<bool> NameExistsAsync(string name, int? excludeCategoryId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<int, int>> GetProductCountsAsync(CancellationToken cancellationToken = default);

    Task<bool> HasProductsAsync(int categoryId, CancellationToken cancellationToken = default);

    void Add(Category category);

    void Update(Category category);

    void Remove(Category category);
}
