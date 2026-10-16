using InventorySystem.Products.Application.Abstractions;
using InventorySystem.Products.Application.Dtos;
using InventorySystem.Products.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Products.Infrastructure.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly ProductDbContext _dbContext;

    public ProductRepository(ProductDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        _dbContext.Products
            .Include(product => product.Category)
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

    public Task<Product?> GetBySkuAsync(string sku, CancellationToken cancellationToken = default)
    {
        var normalized = Product.NormalizeSku(sku);

        return _dbContext.Products
            .Include(product => product.Category)
            .FirstOrDefaultAsync(product => product.Sku == normalized, cancellationToken);
    }

    public Task<bool> SkuExistsAsync(string sku, int? excludeProductId = null, CancellationToken cancellationToken = default)
    {
        var normalized = Product.NormalizeSku(sku);

        return _dbContext.Products.AnyAsync(
            product => product.Sku == normalized
                       && (excludeProductId == null || product.Id != excludeProductId),
            cancellationToken);
    }

    public async Task<PagedResult<Product>> ListAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        var normalizedQuery = query.Normalize();

        var source = _dbContext.Products
            .Include(product => product.Category)
            .AsNoTracking()
            .AsQueryable();

        if (!normalizedQuery.IncludeInactive)
        {
            source = source.Where(product => product.IsActive);
        }

        if (normalizedQuery.CategoryId is { } categoryId)
        {
            source = source.Where(product => product.CategoryId == categoryId);
        }

        if (normalizedQuery.Search is { } search)
        {
            // Portable case-insensitive search across SQL Server and SQLite.
            var term = search.ToUpperInvariant();
            source = source.Where(product =>
                product.Sku.ToUpper().Contains(term) || product.Name.ToUpper().Contains(term));
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderBy(product => product.Name)
            .ThenBy(product => product.Id)
            .Skip((normalizedQuery.Page - 1) * normalizedQuery.PageSize)
            .Take(normalizedQuery.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Product>(items, normalizedQuery.Page, normalizedQuery.PageSize, totalCount);
    }

    public async Task<IReadOnlyList<Product>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToArray();

        return await _dbContext.Products
            .Include(product => product.Category)
            .AsNoTracking()
            .Where(product => idList.Contains(product.Id))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountActiveAsync(CancellationToken cancellationToken = default) =>
        _dbContext.Products.CountAsync(product => product.IsActive, cancellationToken);

    public void Add(Product product) => _dbContext.Products.Add(product);

    public void Update(Product product) => _dbContext.Products.Update(product);
}
