using InventorySystem.Alerts.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Alerts.Infrastructure.Persistence.Repositories;

public class ProcessedEventStore : IProcessedEventStore
{
    private readonly AlertDbContext _dbContext;

    public ProcessedEventStore(AlertDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        _dbContext.ProcessedEvents.AnyAsync(processed => processed.EventId == eventId, cancellationToken);

    public void Add(Guid eventId, string eventName, DateTime processedAt) =>
        _dbContext.ProcessedEvents.Add(new ProcessedEvent(eventId, eventName, processedAt));
}

public class UnitOfWork : IUnitOfWork
{
    private readonly AlertDbContext _dbContext;

    public UnitOfWork(AlertDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
