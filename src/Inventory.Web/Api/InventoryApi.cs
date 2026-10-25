using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Api;

/// <summary>Stock levels, the ledger and the four stock operations.</summary>
public interface IInventoryApi
{
    Task<PagedResult<InventoryDto>> GetStockAsync(InventoryQuery query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryDto>> GetStockByProductAsync(int productId, CancellationToken cancellationToken = default);

    Task<PagedResult<InventoryTransactionDto>> GetHistoryAsync(HistoryQuery query, CancellationToken cancellationToken = default);

    Task<InventoryDto> ReceiveAsync(ReceiveStockRequest request, CancellationToken cancellationToken = default);

    Task<InventoryDto> IssueAsync(IssueStockRequest request, CancellationToken cancellationToken = default);

    Task<TransferResultDto> TransferAsync(TransferStockRequest request, CancellationToken cancellationToken = default);

    Task<InventoryDto> AdjustAsync(AdjustStockRequest request, CancellationToken cancellationToken = default);
}

public sealed class InventoryApi : ApiClientBase, IInventoryApi
{
    public InventoryApi(HttpClient httpClient, ILogger<InventoryApi> logger)
        : base(httpClient, logger, "Inventory service")
    {
    }

    public Task<PagedResult<InventoryDto>> GetStockAsync(
        InventoryQuery query,
        CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<InventoryDto>>(
            "/api/inventory" + ApiQuery.Build(
                ("productId", query.ProductId),
                ("warehouseId", query.WarehouseId),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            cancellationToken);

    public Task<IReadOnlyList<InventoryDto>> GetStockByProductAsync(
        int productId,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<InventoryDto>>($"/api/inventory/{productId}", cancellationToken);

    public Task<PagedResult<InventoryTransactionDto>> GetHistoryAsync(
        HistoryQuery query,
        CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history" + ApiQuery.Build(
                ("productId", query.ProductId),
                ("warehouseId", query.WarehouseId),
                ("type", query.Type),
                ("from", query.From),
                ("to", query.To),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            cancellationToken);

    public Task<InventoryDto> ReceiveAsync(
        ReceiveStockRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<InventoryDto>("/api/inventory/receive", request, cancellationToken);

    public Task<InventoryDto> IssueAsync(
        IssueStockRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<InventoryDto>("/api/inventory/issue", request, cancellationToken);

    public Task<TransferResultDto> TransferAsync(
        TransferStockRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<TransferResultDto>("/api/inventory/transfer", request, cancellationToken);

    public Task<InventoryDto> AdjustAsync(
        AdjustStockRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<InventoryDto>("/api/inventory/adjust", request, cancellationToken);
}
