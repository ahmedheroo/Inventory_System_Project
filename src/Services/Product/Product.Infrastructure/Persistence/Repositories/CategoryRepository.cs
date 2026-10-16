using InventorySystem.Products.Application.Abstractions;
using InventorySystem.Products.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Products.Infrastructure.Persistence.Repositories;

public class CategoryRepository : ICategoryRepository
{
    private readonly ProductDbContext _dbContext;

    public CategoryRepository(ProductDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Category?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _dbContext.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

    public Task<bool> NameExistsAsync(string name, int? excludeCategoryId = null, CancellationToken cancellationToken = default)
    {
        var normalized = name.Trim().ToUpperInvariant();

        return _dbContext.Categories.AnyAsync(
            category => category.Name.ToUpper() == normalized
                        && (excludeCategoryId == null || category.Id != excludeCategoryId),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Category>> ListAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Categories
            .AsNoTracking()
            .OrderBy(category => category.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyDictionary<int, int>> GetProductCountsAsync(CancellationToken cancellationToken = default)
    {
        var counts = await _dbContext.Products
            .AsNoTracking()
            .Where(product => product.CategoryId != null)
            .GroupBy(product => product.CategoryId!.Value)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(item => item.CategoryId, item => item.Count);
    }

    public Task<bool> HasProductsAsync(int categoryId, CancellationToken cancellationToken = default) =>
        _dbContext.Products.AnyAsync(product => product.CategoryId == categoryId, cancellationToken);

    public void Add(Category category) => _dbContext.Categories.Add(category);

    public void Update(Category category) => _dbContext.Categories.Update(category);

    public void Remove(Category category) => _dbContext.Categories.Remove(category);
}
