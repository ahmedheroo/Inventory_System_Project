using InventorySystem.Reports.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Reports.Infrastructure.Persistence.Repositories;

public class ProcessedEventStore : IProcessedEventStore
{
    private readonly ReportingDbContext _dbContext;

    public ProcessedEventStore(ReportingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default) =>
        _dbContext.ProcessedEvents.AnyAsync(processed => processed.EventId == eventId, cancellationToken);

    public void Add(Guid eventId, string eventName, DateTime processedAt) =>
        _dbContext.ProcessedEvents.Add(new ProcessedEvent(eventId, eventName, processedAt));
}

/// <summary>Commits one event's projection (stock levels, movements, de-duplication) together.</summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ReportingDbContext _dbContext;

    public UnitOfWork(ReportingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
