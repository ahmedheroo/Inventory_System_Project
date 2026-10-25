using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Api;

/// Low-stock alerts. They are raised by the Alert service's consumer, so the console only reads and acknowledges them.
public interface IAlertsApi
{
    Task<PagedResult<AlertDto>> GetAlertsAsync(AlertQuery query, CancellationToken cancellationToken = default);

    Task<AlertDto?> GetAlertAsync(int id, CancellationToken cancellationToken = default);

    Task<AlertDto> MarkAsReadAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class AlertsApi : ApiClientBase, IAlertsApi
{
    public AlertsApi(HttpClient httpClient, ILogger<AlertsApi> logger)
        : base(httpClient, logger, "Alert service")
    {
    }

    public Task<PagedResult<AlertDto>> GetAlertsAsync(
        AlertQuery query,
        CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<AlertDto>>(
            "/api/alerts" + ApiQuery.Build(
                ("productId", query.ProductId),
                ("warehouseId", query.WarehouseId),
                ("type", query.Type),
                ("isRead", query.IsRead),
                ("status", query.Status),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            cancellationToken);

    public Task<AlertDto?> GetAlertAsync(int id, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<AlertDto>($"/api/alerts/{id}", cancellationToken);

    public Task<AlertDto> MarkAsReadAsync(int id, CancellationToken cancellationToken = default) =>
        PutAsync<AlertDto>($"/api/alerts/{id}/read", body: null, cancellationToken);
}
