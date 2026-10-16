using InventorySystem.Alerts.Application.Abstractions;
using InventorySystem.Alerts.Application.Dtos;
using InventorySystem.Alerts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Alerts.Infrastructure.Persistence.Repositories;

public class AlertRepository : IAlertRepository
{
    private readonly AlertDbContext _dbContext;

    public AlertRepository(AlertDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Alert?> GetAsync(int id, CancellationToken cancellationToken = default) =>
        _dbContext.Alerts.FirstOrDefaultAsync(alert => alert.Id == id, cancellationToken);

    public Task<Alert?> FindActiveAsync(int productId, int warehouseId, CancellationToken cancellationToken = default) =>
        _dbContext.Alerts
            .Where(alert => alert.ProductId == productId
                            && alert.WarehouseId == warehouseId
                            && alert.Status == AlertStatus.ACTIVE)
            .OrderByDescending(alert => alert.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<Alert>> ListAsync(AlertQuery query, CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();

        var source = _dbContext.Alerts.AsNoTracking().AsQueryable();

        if (normalized.ProductId is { } productId)
        {
            source = source.Where(alert => alert.ProductId == productId);
        }

        if (normalized.WarehouseId is { } warehouseId)
        {
            source = source.Where(alert => alert.WarehouseId == warehouseId);
        }

        if (normalized.Type is { } type)
        {
            source = source.Where(alert => alert.Type == type);
        }

        if (normalized.IsRead is { } isRead)
        {
            source = source.Where(alert => alert.IsRead == isRead);
        }

        if (normalized.Status is { } status)
        {
            source = source.Where(alert => alert.Status == status);
        }

        var totalCount = await source.CountAsync(cancellationToken);

        var items = await source
            .OrderByDescending(alert => alert.CreatedAt)
            .ThenByDescending(alert => alert.Id)
            .Skip((normalized.Page - 1) * normalized.PageSize)
            .Take(normalized.PageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<Alert>(items, normalized.Page, normalized.PageSize, totalCount);
    }

    public void Add(Alert alert) => _dbContext.Alerts.Add(alert);
}
