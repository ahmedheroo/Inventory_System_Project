using InventorySystem.Alerts.Application.Abstractions;
using InventorySystem.Alerts.Application.Dtos;
using InventorySystem.Alerts.Application.Exceptions;
using InventorySystem.Alerts.Domain.Entities;

namespace InventorySystem.Alerts.Application.Services;

/// <summary>
/// Read and acknowledge use cases of the Alert service (spec section 7.3:
/// GET /api/alerts, GET /api/alerts/{id}, PUT /api/alerts/{id}/read).
/// Alerts are created by <see cref="InventoryEventProcessor"/>, never here.
/// </summary>
public class AlertService
{
    private readonly IAlertRepository _alerts;
    private readonly IUnitOfWork _unitOfWork;

    public AlertService(IAlertRepository alerts, IUnitOfWork unitOfWork)
    {
        _alerts = alerts;
        _unitOfWork = unitOfWork;
    }

    public async Task<PagedResult<AlertDto>> GetAsync(AlertQuery query, CancellationToken cancellationToken = default)
    {
        var page = await _alerts.ListAsync(query.Normalize(), cancellationToken);

        return new PagedResult<AlertDto>(
            page.Items.Select(ToDto).ToList(),
            page.Page,
            page.PageSize,
            page.TotalCount);
    }

    public async Task<AlertDto> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var alert = await RequireAsync(id, cancellationToken);

        return ToDto(alert);
    }

    /// <summary>
    /// Marks an alert as read. Idempotent: reading an already-read alert is a
    /// success and keeps the original timestamp, which makes the endpoint safe to
    /// retry.
    /// </summary>
    public async Task<AlertDto> MarkAsReadAsync(int id, CancellationToken cancellationToken = default)
    {
        var alert = await RequireAsync(id, cancellationToken);

        if (alert.MarkAsRead())
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return ToDto(alert);
    }

    private async Task<Alert> RequireAsync(int id, CancellationToken cancellationToken) =>
        await _alerts.GetAsync(id, cancellationToken)
        ?? throw new NotFoundException($"Alert with id '{id}' does not exist.");

    private static AlertDto ToDto(Alert alert) => new(
        alert.Id,
        alert.ProductId,
        alert.WarehouseId,
        alert.Type,
        alert.Message,
        alert.CurrentQuantity,
        alert.MinimumStockLevel,
        alert.IsRead,
        alert.Status,
        alert.CreatedAt,
        alert.UpdatedAt,
        alert.ReadAt,
        alert.ResolvedAt);
}
