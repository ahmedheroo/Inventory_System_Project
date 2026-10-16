using InventorySystem.Products.Application.Abstractions;
using InventorySystem.Products.Application.Dtos;
using InventorySystem.Products.Application.Exceptions;
using InventorySystem.Products.Domain.Entities;

namespace InventorySystem.Products.Application.Services;

/// Category use cases 
public class CategoryService
{
    private readonly ICategoryRepository _categories;
    private readonly IUnitOfWork _unitOfWork;

    public CategoryService(ICategoryRepository categories, IUnitOfWork unitOfWork)
    {
        _categories = categories;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<CategoryDto>> GetAsync(CancellationToken cancellationToken = default)
    {
        var categories = await _categories.ListAsync(cancellationToken);
        var counts = await _categories.GetProductCountsAsync(cancellationToken);

        return categories
            .Select(category => ToDto(category, counts.TryGetValue(category.Id, out var count) ? count : 0))
            .ToList();
    }

    public async Task<CategoryDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await _categories.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Category", id);

        var counts = await _categories.GetProductCountsAsync(cancellationToken);
        return ToDto(category, counts.TryGetValue(category.Id, out var count) ? count : 0);
    }

    public async Task<CategoryDto> CreateAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var name = request.Name.Trim();

        if (await _categories.NameExistsAsync(name, null, cancellationToken))
        {
            throw new ValidationException($"Category '{name}' already exists.");
        }

        var category = Category.Create(name, request.Description);
        _categories.Add(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(category, 0);
    }

    public async Task<CategoryDto> UpdateAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        var category = await _categories.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Category", id);

        var name = request.Name.Trim();
        if (await _categories.NameExistsAsync(name, id, cancellationToken))
        {
            throw new ValidationException($"Category '{name}' already exists.");
        }

        category.Update(name, request.Description);
        _categories.Update(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var counts = await _categories.GetProductCountsAsync(cancellationToken);
        return ToDto(category, counts.TryGetValue(category.Id, out var count) ? count : 0);
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var category = await _categories.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Category", id);

        if (await _categories.HasProductsAsync(id, cancellationToken))
        {
            throw new ValidationException(
                "Category still has products assigned to it. Move or deactivate those products first.");
        }

        _categories.Remove(category);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private static CategoryDto ToDto(Category category, int productCount) => new(
        category.Id,
        category.Name,
        category.Description,
        productCount,
        category.CreatedAt,
        category.UpdatedAt);
}
