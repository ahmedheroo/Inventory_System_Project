using InventorySystem.Products.Application.Abstractions;
using InventorySystem.Products.Application.Dtos;
using InventorySystem.Products.Application.Exceptions;
using InventorySystem.Products.Domain.Entities;

namespace InventorySystem.Products.Application.Services;


/// Product use cases: create, update, retrieve, search,
/// deactivate, validation.

public class ProductService
{
    private readonly IProductRepository _products;
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;

    public ProductService(
        IProductRepository products,
        ICategoryRepository categories,
        IUnitOfWork unitOfWork)
    {
        _products = products;
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<ProductDto>> GetAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _products.ListAsync(query.Normalize(), cancellationToken);

        return new PagedResult<ProductDto>(
            page.Items.Select(ToDto).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<ProductDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Product", id);

        return ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        var sku = Product.NormalizeSku(request.Sku);

        if (await _products.SkuExistsAsync(sku, null, cancellationToken))
        {
            throw new ValidationException($"SKU '{sku}' is already used by another product.");
        }

        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

        var product = Product.Create(
            sku,
            request.Name,
            request.Description,
            request.CategoryId,
            request.UnitPrice,
            request.MinimumStockLevel);

        _products.Add(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(product.Id, cancellationToken);
    }

    public async Task<ProductDto> UpdateAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Product", id);

        await EnsureCategoryExistsAsync(request.CategoryId, cancellationToken);

        product.Update(
            request.Name,
            request.Description,
            request.CategoryId,
            request.UnitPrice,
            request.MinimumStockLevel);

        _products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(product.Id, cancellationToken);
    }

    public async Task<ProductDto> ChangeSkuAsync(int id, string sku, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Product", id);

        var normalized = Product.NormalizeSku(sku);
        if (await _products.SkuExistsAsync(normalized, id, cancellationToken))
        {
            throw new ValidationException($"SKU '{normalized}' is already used by another product.");
        }

        product.ChangeSku(normalized);
        _products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(product);
    }

    ///Soft delete: the API's DELETE marks the product inactive 
    public async Task DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Product", id);

        product.Deactivate();
        _products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<ProductDto> ActivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Product", id);

        product.Activate();
        _products.Update(product);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(product);
    }

    private async Task EnsureCategoryExistsAsync(int? categoryId, CancellationToken cancellationToken)
    {
        if (categoryId is null)
        {
            return;
        }

        var category = await _categories.GetByIdAsync(categoryId.Value, cancellationToken);
        if (category is null)
        {
            throw new ValidationException($"Category with id '{categoryId}' does not exist.");
        }
    }

    private static ProductDto ToDto(Product product) => new(
        product.Id,
        product.Sku,
        product.Name,
        product.Description,
        product.CategoryId,
        product.Category?.Name,
        product.UnitPrice,
        product.MinimumStockLevel,
        product.IsActive,
        product.CreatedAt,
        product.UpdatedAt);
}
