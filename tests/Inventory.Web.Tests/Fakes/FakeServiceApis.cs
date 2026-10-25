using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Models;
using Inventory.Web.Services;

namespace InventorySystem.Web.Tests.Fakes;

/// <summary>
/// The four service clients, faked. The console's tests never need a running service:
/// they need to know which call the console made, with which filters, and what it does
/// with the answer.
/// </summary>
public sealed class FakeCatalogApi : ICatalogApi
{
    public List<ProductDto> Products { get; } = [];

    public List<CategoryDto> Categories { get; } = [];

    public List<WarehouseDto> Warehouses { get; } = [];

    /// <summary>What the last product list call asked for.</summary>
    public ProductQuery? LastQuery { get; private set; }

    public ApiException? FailNextWith { get; set; }

    public Task<PagedResult<ProductDto>> GetProductsAsync(ProductQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;

        var matched = Products
            .Where(product => query.IncludeInactive || product.IsActive)
            .Where(product => query.CategoryId is null || product.CategoryId == query.CategoryId)
            .Where(product => string.IsNullOrWhiteSpace(query.Search)
                              || product.Sku.Contains(query.Search, StringComparison.OrdinalIgnoreCase)
                              || product.Name.Contains(query.Search, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult(new PagedResult<ProductDto>(
            matched.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(),
            query.Page,
            query.PageSize,
            matched.Count));
    }

    public Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Products.FirstOrDefault(product => product.Id == id));

    public Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default)
    {
        Throw();

        var product = new ProductDto(
            Products.Count + 1,
            request.Sku,
            request.Name,
            request.Description,
            request.CategoryId,
            Categories.FirstOrDefault(category => category.Id == request.CategoryId)?.Name,
            request.UnitPrice,
            request.MinimumStockLevel,
            true,
            DateTime.UtcNow,
            DateTime.UtcNow);

        Products.Add(product);

        return Task.FromResult(product);
    }

    public Task<ProductDto> UpdateProductAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default)
    {
        Throw();

        var product = Products.First(product => product.Id == id);

        return Task.FromResult(product);
    }

    public Task<ProductDto> ChangeSkuAsync(int id, string sku, CancellationToken cancellationToken = default)
    {
        Throw();

        return Task.FromResult(Products.First(product => product.Id == id) with { Sku = sku });
    }

    public Task DeactivateProductAsync(int id, CancellationToken cancellationToken = default)
    {
        Throw();

        var index = Products.FindIndex(product => product.Id == id);
        Products[index] = Products[index] with { IsActive = false };

        return Task.CompletedTask;
    }

    public Task<ProductDto> ActivateProductAsync(int id, CancellationToken cancellationToken = default)
    {
        Throw();

        var index = Products.FindIndex(product => product.Id == id);
        Products[index] = Products[index] with { IsActive = true };

        return Task.FromResult(Products[index]);
    }

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<CategoryDto>>(Categories);

    public Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        Throw();

        var category = new CategoryDto(Categories.Count + 1, request.Name, request.Description, 0, DateTime.UtcNow, DateTime.UtcNow);
        Categories.Add(category);

        return Task.FromResult(category);
    }

    public Task<CategoryDto> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default)
    {
        Throw();

        var category = Categories.First(candidate => candidate.Id == id) with { Name = request.Name, Description = request.Description };

        return Task.FromResult(category);
    }

    public Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default)
    {
        Throw();

        Categories.RemoveAll(category => category.Id == id);

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(bool includeInactive = false, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WarehouseDto>>(
            Warehouses.Where(warehouse => includeInactive || warehouse.IsActive).ToList());

    public Task<WarehouseDto?> GetWarehouseAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Warehouses.FirstOrDefault(warehouse => warehouse.Id == id));

    public Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        Throw();

        var warehouse = new WarehouseDto(
            Warehouses.Count + 1,
            request.Code,
            request.Name,
            request.Address,
            true,
            DateTime.UtcNow,
            DateTime.UtcNow);

        Warehouses.Add(warehouse);

        return Task.FromResult(warehouse);
    }

    public Task<WarehouseDto> UpdateWarehouseAsync(int id, UpdateWarehouseRequest request, CancellationToken cancellationToken = default)
    {
        Throw();

        var warehouse = Warehouses.First(candidate => candidate.Id == id);

        return Task.FromResult(warehouse);
    }

    public Task DeactivateWarehouseAsync(int id, CancellationToken cancellationToken = default)
    {
        Throw();

        var index = Warehouses.FindIndex(warehouse => warehouse.Id == id);
        Warehouses[index] = Warehouses[index] with { IsActive = false };

        return Task.CompletedTask;
    }

    public Task<WarehouseDto> ActivateWarehouseAsync(int id, CancellationToken cancellationToken = default)
    {
        Throw();

        var index = Warehouses.FindIndex(warehouse => warehouse.Id == id);
        Warehouses[index] = Warehouses[index] with { IsActive = true };

        return Task.FromResult(Warehouses[index]);
    }

    private void Throw()
    {
        if (FailNextWith is { } failure)
        {
            FailNextWith = null;

            throw failure;
        }
    }
}

public sealed class FakeInventoryApi : IInventoryApi
{
    public List<InventoryDto> Stock { get; } = [];

    public List<InventoryTransactionDto> Ledger { get; } = [];

    public InventoryQuery? LastQuery { get; private set; }

    public HistoryQuery? LastHistoryQuery { get; private set; }

    public ReceiveStockRequest? LastReceive { get; private set; }

    public IssueStockRequest? LastIssue { get; private set; }

    public TransferStockRequest? LastTransfer { get; private set; }

    public AdjustStockRequest? LastAdjust { get; private set; }

    /// <summary>A refusal the service would answer with, e.g. an issue larger than the level on hand.</summary>
    public ApiException? RefuseWith { get; set; }

    public Task<PagedResult<InventoryDto>> GetStockAsync(InventoryQuery query, CancellationToken cancellationToken = default)
    {
        LastQuery = query;

        var matched = Stock
            .Where(row => query.ProductId is null || row.ProductId == query.ProductId)
            .Where(row => query.WarehouseId is null || row.WarehouseId == query.WarehouseId)
            .OrderBy(row => row.ProductId)
            .ThenBy(row => row.WarehouseId)
            .ToList();

        return Task.FromResult(new PagedResult<InventoryDto>(
            matched.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(),
            query.Page,
            query.PageSize,
            matched.Count));
    }

    public Task<IReadOnlyList<InventoryDto>> GetStockByProductAsync(int productId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InventoryDto>>(Stock.Where(row => row.ProductId == productId).ToList());

    public Task<PagedResult<InventoryTransactionDto>> GetHistoryAsync(HistoryQuery query, CancellationToken cancellationToken = default)
    {
        LastHistoryQuery = query;

        var matched = Ledger
            .Where(entry => query.ProductId is null || entry.ProductId == query.ProductId)
            .Where(entry => query.WarehouseId is null || entry.WarehouseId == query.WarehouseId)
            .Where(entry => query.Type is null || entry.Type == query.Type)
            .Where(entry => query.From is null || entry.CreatedAt >= query.From)
            .Where(entry => query.To is null || entry.CreatedAt <= query.To)
            .OrderByDescending(entry => entry.CreatedAt)
            .ToList();

        return Task.FromResult(new PagedResult<InventoryTransactionDto>(
            matched.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(),
            query.Page,
            query.PageSize,
            matched.Count));
    }

    public Task<InventoryDto> ReceiveAsync(ReceiveStockRequest request, CancellationToken cancellationToken = default)
    {
        LastReceive = request;

        return Task.FromResult(Move(request.ProductId, request.WarehouseId, request.Quantity, StockTransactionType.RECEIVE));
    }

    public Task<InventoryDto> IssueAsync(IssueStockRequest request, CancellationToken cancellationToken = default)
    {
        LastIssue = request;

        return Task.FromResult(Move(request.ProductId, request.WarehouseId, -request.Quantity, StockTransactionType.ISSUE));
    }

    public Task<TransferResultDto> TransferAsync(TransferStockRequest request, CancellationToken cancellationToken = default)
    {
        LastTransfer = request;

        var source = Move(request.ProductId, request.SourceWarehouseId, -request.Quantity, StockTransactionType.TRANSFER_OUT);
        var destination = Move(request.ProductId, request.DestinationWarehouseId, request.Quantity, StockTransactionType.TRANSFER_IN);

        return Task.FromResult(new TransferResultDto(request.ProductId, request.Quantity, source, destination));
    }

    public Task<InventoryDto> AdjustAsync(AdjustStockRequest request, CancellationToken cancellationToken = default)
    {
        LastAdjust = request;

        var row = Row(request.ProductId, request.WarehouseId);
        var difference = request.NewQuantity - row.Quantity;
        var updated = row with { Quantity = request.NewQuantity, UpdatedAt = DateTime.UtcNow };

        Replace(updated);
        Ledger.Add(new InventoryTransactionDto(
            Ledger.Count + 1,
            request.ProductId,
            request.WarehouseId,
            StockTransactionType.ADJUSTMENT,
            difference,
            difference,
            request.Reference,
            DateTime.UtcNow));

        return Task.FromResult(updated);
    }

    private InventoryDto Move(int productId, int warehouseId, int quantity, StockTransactionType type)
    {
        if (RefuseWith is { } refusal)
        {
            RefuseWith = null;

            throw refusal;
        }

        var row = Row(productId, warehouseId);
        var updated = row with { Quantity = row.Quantity + quantity, UpdatedAt = DateTime.UtcNow };

        Replace(updated);
        Ledger.Add(new InventoryTransactionDto(
            Ledger.Count + 1,
            productId,
            warehouseId,
            type,
            Math.Abs(quantity),
            quantity,
            null,
            DateTime.UtcNow));

        return updated;
    }

    private InventoryDto Row(int productId, int warehouseId) =>
        Stock.FirstOrDefault(row => row.ProductId == productId && row.WarehouseId == warehouseId)
        ?? new InventoryDto(0, productId, warehouseId, 0, DateTime.UtcNow);

    private void Replace(InventoryDto row)
    {
        var index = Stock.FindIndex(candidate => candidate.Id == row.Id && row.Id != 0);

        if (index >= 0)
        {
            Stock[index] = row;
        }
        else
        {
            Stock.Add(row with { Id = Stock.Count == 0 ? 1 : Stock.Max(candidate => candidate.Id) + 1 });
        }
    }
}

public sealed class FakeAlertsApi : IAlertsApi
{
    public List<AlertDto> Alerts { get; } = [];

    public AlertQuery? LastQuery { get; private set; }

    public List<int> Acknowledged { get; } = [];

    /// <summary>An Alert service that is down, for the screens that must say so instead of showing a zero.</summary>
    public ApiException? FailWith { get; set; }

    public Task<PagedResult<AlertDto>> GetAlertsAsync(AlertQuery query, CancellationToken cancellationToken = default)
    {
        if (FailWith is { } failure)
        {
            throw failure;
        }

        LastQuery = query;

        var matched = Alerts
            .Where(alert => query.Status is null || alert.Status == query.Status)
            .Where(alert => query.Type is null || alert.Type == query.Type)
            .Where(alert => query.IsRead is null || alert.IsRead == query.IsRead)
            .Where(alert => query.WarehouseId is null || alert.WarehouseId == query.WarehouseId)
            .Where(alert => query.ProductId is null || alert.ProductId == query.ProductId)
            .ToList();

        return Task.FromResult(new PagedResult<AlertDto>(
            matched.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList(),
            query.Page,
            query.PageSize,
            matched.Count));
    }

    public Task<AlertDto?> GetAlertAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Alerts.FirstOrDefault(alert => alert.Id == id));

    public Task<AlertDto> MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
    {
        Acknowledged.Add(id);

        var index = Alerts.FindIndex(alert => alert.Id == id);
        Alerts[index] = Alerts[index] with { IsRead = true, ReadAt = DateTime.UtcNow };

        return Task.FromResult(Alerts[index]);
    }
}

public sealed class FakeReportingApi : IReportingApi
{
    public InventoryReport InventoryReport { get; set; } = new([], new(0, 0, 0, 0, 0m, 0, 0), 1, 20, 0);

    public LowStockReport LowStockReport { get; set; } = new([], new(0, 0, 0, 0, 0), 1, 20, 0);

    public MovementReport MovementReport { get; set; } = new([], new(0, 0, 0, 0, 0, 0, 0), 1, 20, 0);

    public ValuationReport ValuationReport { get; set; } = new([], [], new(0, 0, 0, 0, 0m), 1, 20, 0);

    public (int? ProductId, int? WarehouseId, int Page, int PageSize)? LastInventoryCall { get; private set; }

    public (int? ProductId, int? WarehouseId, bool IncludeOutOfStock, int Page, int PageSize)? LastLowStockCall { get; private set; }

    public (int? ProductId, int? WarehouseId, StockTransactionType? Type, DateTime? From, DateTime? To)? LastMovementCall { get; private set; }

    public (int? WarehouseId, int? CategoryId)? LastValuationCall { get; private set; }

    /// <summary>The path and query of the last download, exactly as the console forwarded it.</summary>
    public string? LastDownload { get; private set; }

    public ApiException? FailWith { get; set; }

    public Task<InventoryReport> GetInventoryReportAsync(
        int? productId = null,
        int? warehouseId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastInventoryCall = (productId, warehouseId, page, pageSize);

        // The service echoes the page it served, which is what the pager is built from.
        return Task.FromResult(InventoryReport with { Page = page, PageSize = pageSize });
    }

    public Task<LowStockReport> GetLowStockReportAsync(
        int? productId = null,
        int? warehouseId = null,
        bool includeOutOfStock = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastLowStockCall = (productId, warehouseId, includeOutOfStock, page, pageSize);

        return Task.FromResult(LowStockReport with { Page = page, PageSize = pageSize });
    }

    public Task<MovementReport> GetMovementReportAsync(
        int? productId = null,
        int? warehouseId = null,
        StockTransactionType? type = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastMovementCall = (productId, warehouseId, type, from, to);

        return Task.FromResult(MovementReport with { Page = page, PageSize = pageSize });
    }

    public Task<ValuationReport> GetValuationReportAsync(
        int? warehouseId = null,
        int? categoryId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Throw();

        LastValuationCall = (warehouseId, categoryId);

        return Task.FromResult(ValuationReport with { Page = page, PageSize = pageSize });
    }

    public Task<DownloadedFile> DownloadAsync(string report, string query, CancellationToken cancellationToken = default)
    {
        Throw();

        LastDownload = $"/api/reports/{report}/export{query}";

        return Task.FromResult(new DownloadedFile($"{report}-report.csv", "text/csv", [1, 2, 3]));
    }

    private void Throw()
    {
        if (FailWith is { } failure)
        {
            FailWith = null;

            throw failure;
        }
    }
}

/// <summary>The catalogue as one request sees it: built from a fake Product service, cached per test.</summary>
public sealed class FakeCatalogLookup : ICatalogLookupService
{
    private readonly ICatalogApi _catalogue;

    public FakeCatalogLookup(ICatalogApi catalogue)
    {
        _catalogue = catalogue;
    }

    public int Reads { get; private set; }

    public async Task<CatalogLookup> GetAsync(CancellationToken cancellationToken = default)
    {
        Reads++;

        var products = await _catalogue.GetProductsAsync(
            new ProductQuery { IncludeInactive = true, Page = 1, PageSize = 100 },
            cancellationToken);

        var warehouses = await _catalogue.GetWarehousesAsync(includeInactive: true, cancellationToken);

        return new CatalogLookup(
            products.Items.ToDictionary(product => product.Id),
            warehouses.ToDictionary(warehouse => warehouse.Id));
    }
}
