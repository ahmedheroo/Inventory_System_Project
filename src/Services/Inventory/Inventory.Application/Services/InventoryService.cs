using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Catalog;
using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Application.Exceptions;
using InventorySystem.Stock.Domain.Entities;

namespace InventorySystem.Stock.Application.Services;

 
public class InventoryService
{
    private readonly IInventoryRepository _inventory;
    private readonly IInventoryTransactionRepository _transactions;
    private readonly IProductCatalogClient _catalog;
    private readonly IEventOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public InventoryService(
        IInventoryRepository inventory,
        IInventoryTransactionRepository transactions,
        IProductCatalogClient catalog,
        IEventOutbox outbox,
        IUnitOfWork unitOfWork)
    {
        _inventory = inventory;
        _transactions = transactions;
        _catalog = catalog;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<InventoryDto>> GetAsync(InventoryQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _inventory.ListAsync(query.Normalize(), cancellationToken);

        return new PagedResult<InventoryDto>(
            page.Items.Select(ToDto).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    ///  Stock of one product across every warehouse (spec: GET /api/inventory/{productId}). 
    public async Task<IReadOnlyList<InventoryDto>> GetByProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        var rows = await _inventory.ListByProductAsync(productId, cancellationToken);

        return rows.Select(ToDto).ToList();
    }

    public async Task<PagedResult<InventoryTransactionDto>> GetHistoryAsync(HistoryQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _transactions.ListAsync(query.Normalize(), cancellationToken);

        return new PagedResult<InventoryTransactionDto>(
            page.Items.Select(ToDto).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<InventoryDto> ReceiveAsync(ReceiveStockRequest request, CancellationToken cancellationToken = default)
    {
        var product = await RequireProductAsync(request.ProductId, cancellationToken);
        await RequireWarehouseAsync(request.WarehouseId, cancellationToken);

        var inventory = await GetOrCreateAsync(request.ProductId, request.WarehouseId, cancellationToken);
        var newQuantity = inventory.Receive(request.Quantity);

        _transactions.Add(InventoryTransaction.Create(
            request.ProductId,
            request.WarehouseId,
            StockTransactionType.RECEIVE,
            request.Quantity,
            request.Reference));

        _outbox.Enqueue(new StockReceivedEvent
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            Quantity = request.Quantity,
            CurrentQuantity = newQuantity,
            MinimumStockLevel = product.MinimumStockLevel
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(inventory);
    }

    public async Task<InventoryDto> IssueAsync(IssueStockRequest request, CancellationToken cancellationToken = default)
    {
        var product = await RequireProductAsync(request.ProductId, cancellationToken);
        await RequireWarehouseAsync(request.WarehouseId, cancellationToken);

        // Rule 2: an issue beyond what is on hand is refused by the aggregate.
        var inventory = await _inventory.GetAsync(request.ProductId, request.WarehouseId, cancellationToken)
            ?? throw new ValidationException(
                $"Product {request.ProductId} has no stock in warehouse {request.WarehouseId}.");

        var newQuantity = inventory.Issue(request.Quantity);

        _transactions.Add(InventoryTransaction.Create(
            request.ProductId,
            request.WarehouseId,
            StockTransactionType.ISSUE,
            request.Quantity,
            request.Reference));

        _outbox.Enqueue(new StockIssuedEvent
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            Quantity = request.Quantity,
            CurrentQuantity = newQuantity,
            MinimumStockLevel = product.MinimumStockLevel
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(inventory);
    }

    public async Task<TransferResultDto> TransferAsync(TransferStockRequest request, CancellationToken cancellationToken = default)
    {
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
        {
            throw new ValidationException("Source and destination warehouse must be different.");
        }

        var product = await RequireProductAsync(request.ProductId, cancellationToken);
        var sourceWarehouse = await RequireWarehouseAsync(request.SourceWarehouseId, cancellationToken);
        var destinationWarehouse = await RequireWarehouseAsync(request.DestinationWarehouseId, cancellationToken);

        var source = await _inventory.GetAsync(request.ProductId, request.SourceWarehouseId, cancellationToken)
            ?? throw new ValidationException(
                $"Product {request.ProductId} has no stock in warehouse {request.SourceWarehouseId}.");

        var destination = await GetOrCreateAsync(request.ProductId, request.DestinationWarehouseId, cancellationToken);

        var sourceQuantity = source.TransferOut(request.Quantity);
        var destinationQuantity = destination.TransferIn(request.Quantity);

        // Rule 4: a transfer records both legs.
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();

        _transactions.Add(InventoryTransaction.Create(
            request.ProductId,
            request.SourceWarehouseId,
            StockTransactionType.TRANSFER_OUT,
            request.Quantity,
            reference ?? $"Transfer to {destinationWarehouse.Code} (warehouse {destinationWarehouse.Id})"));

        _transactions.Add(InventoryTransaction.Create(
            request.ProductId,
            request.DestinationWarehouseId,
            StockTransactionType.TRANSFER_IN,
            request.Quantity,
            reference ?? $"Transfer from {sourceWarehouse.Code} (warehouse {sourceWarehouse.Id})"));

        _outbox.Enqueue(new StockTransferredEvent
        {
            ProductId = request.ProductId,
            SourceWarehouseId = request.SourceWarehouseId,
            DestinationWarehouseId = request.DestinationWarehouseId,
            Quantity = request.Quantity,
            SourceCurrentQuantity = sourceQuantity,
            DestinationCurrentQuantity = destinationQuantity,
            MinimumStockLevel = product.MinimumStockLevel
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new TransferResultDto(request.ProductId, request.Quantity, ToDto(source), ToDto(destination));
    }

    public async Task<InventoryDto> AdjustAsync(AdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        var product = await RequireProductAsync(request.ProductId, cancellationToken);
        await RequireWarehouseAsync(request.WarehouseId, cancellationToken);

        var inventory = await GetOrCreateAsync(request.ProductId, request.WarehouseId, cancellationToken);
        var oldQuantity = inventory.Quantity;
        var delta = inventory.Adjust(request.NewQuantity);

        _transactions.Add(InventoryTransaction.Create(
            request.ProductId,
            request.WarehouseId,
            StockTransactionType.ADJUSTMENT,
            delta,
            request.Reference ?? $"Adjusted from {oldQuantity} to {request.NewQuantity}"));

        _outbox.Enqueue(new StockAdjustedEvent
        {
            ProductId = request.ProductId,
            WarehouseId = request.WarehouseId,
            OldQuantity = oldQuantity,
            NewQuantity = request.NewQuantity,
            MinimumStockLevel = product.MinimumStockLevel
        });

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return ToDto(inventory);
    }

    private async Task<Inventory> GetOrCreateAsync(int productId, int warehouseId, CancellationToken cancellationToken)
    {
        var inventory = await _inventory.GetAsync(productId, warehouseId, cancellationToken);
        if (inventory is not null)
        {
            return inventory;
        }

        inventory = Inventory.Create(productId, warehouseId);
        _inventory.Add(inventory);

        return inventory;
    }

    private async Task<ProductInfo> RequireProductAsync(int productId, CancellationToken cancellationToken)
    {
        var product = await _catalog.GetProductAsync(productId, cancellationToken);

        if (product is null)
        {
            throw new ValidationException($"Product with id '{productId}' does not exist.");
        }

        if (!product.IsActive)
        {
            throw new ValidationException($"Product with id '{productId}' is deactivated and cannot move stock.");
        }

        return product;
    }

    private async Task<WarehouseInfo> RequireWarehouseAsync(int warehouseId, CancellationToken cancellationToken)
    {
        var warehouse = await _catalog.GetWarehouseAsync(warehouseId, cancellationToken);

        if (warehouse is null)
        {
            throw new ValidationException($"Warehouse with id '{warehouseId}' does not exist.");
        }

        if (!warehouse.IsActive)
        {
            throw new ValidationException($"Warehouse with id '{warehouseId}' is deactivated and cannot move stock.");
        }

        return warehouse;
    }

    private static InventoryDto ToDto(Inventory inventory) => new(
        inventory.Id,
        inventory.ProductId,
        inventory.WarehouseId,
        inventory.Quantity,
        inventory.UpdatedAt);

    private static InventoryTransactionDto ToDto(InventoryTransaction transaction) => new(
        transaction.Id,
        transaction.ProductId,
        transaction.WarehouseId,
        transaction.Type,
        transaction.Quantity,
        transaction.SignedQuantity,
        transaction.Reference,
        transaction.CreatedAt);
}
