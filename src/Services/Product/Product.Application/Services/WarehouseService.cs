using InventorySystem.Products.Application.Abstractions;
using InventorySystem.Products.Application.Dtos;
using InventorySystem.Products.Application.Exceptions;
using InventorySystem.Products.Domain.Entities;

namespace InventorySystem.Products.Application.Services;


/// Warehouse use cases. The Inventory service stores only
/// WarehouseId and resolves warehouse details through this service's API.

public class WarehouseService
{
    private readonly IWarehouseRepository _warehouses;
    private readonly IUnitOfWork _unitOfWork;

    public WarehouseService(IWarehouseRepository warehouses, IUnitOfWork unitOfWork)
    {
        _warehouses = warehouses;
        _unitOfWork = unitOfWork;
    }

    public async Task<IReadOnlyList<WarehouseDto>> GetAsync(bool includeInactive = false, CancellationToken cancellationToken = default)
    {
        var warehouses = await _warehouses.ListAsync(includeInactive, cancellationToken);
        return warehouses.Select(ToDto).ToList();
    }

    public async Task<WarehouseDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _warehouses.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Warehouse", id);

        return ToDto(warehouse);
    }

    public async Task<WarehouseDto> CreateAsync(CreateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        var code = Warehouse.NormalizeCode(request.Code);

        if (await _warehouses.CodeExistsAsync(code, null, cancellationToken))
        {
            throw new ValidationException($"Warehouse code '{code}' is already in use.");
        }

        var warehouse = Warehouse.Create(code, request.Name, request.Address);
        _warehouses.Add(warehouse);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(warehouse);
    }

    public async Task<WarehouseDto> UpdateAsync(int id, UpdateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        var warehouse = await _warehouses.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Warehouse", id);

        warehouse.Update(request.Name, request.Address);
        _warehouses.Update(warehouse);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(warehouse);
    }

    public async Task DeactivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _warehouses.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Warehouse", id);

        warehouse.Deactivate();
        _warehouses.Update(warehouse);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<WarehouseDto> ActivateAsync(int id, CancellationToken cancellationToken = default)
    {
        var warehouse = await _warehouses.GetByIdAsync(id, cancellationToken)
            ?? throw NotFoundException.For("Warehouse", id);

        warehouse.Activate();
        _warehouses.Update(warehouse);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(warehouse);
    }

    private static WarehouseDto ToDto(Warehouse warehouse) => new(
        warehouse.Id,
        warehouse.Code,
        warehouse.Name,
        warehouse.Address,
        warehouse.IsActive,
        warehouse.CreatedAt,
        warehouse.UpdatedAt);
}
