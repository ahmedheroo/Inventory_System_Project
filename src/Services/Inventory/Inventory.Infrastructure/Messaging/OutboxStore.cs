using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Stock.Infrastructure.Messaging;

/// <summary>
/// The outbox table, reached through the same <see cref="InventoryDbContext"/> as
/// the stock rows it is written with: enqueuing an event does not touch the
/// database on its own, it only joins the transaction the caller is about to
/// commit.
/// </summary>
public class OutboxStore : IEventOutbox
{
    private readonly InventoryDbContext _dbContext;

    public OutboxStore(InventoryDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public void Enqueue(IntegrationEvent integrationEvent) =>
        _dbContext.OutboxMessages.Add(OutboxMessage.For(integrationEvent));

    /// <summary>Pending messages whose backoff has elapsed, oldest first.</summary>
    public async Task<IReadOnlyList<OutboxMessage>> GetPendingAsync(
        DateTime asOf,
        int take,
        CancellationToken cancellationToken = default) =>
        await _dbContext.OutboxMessages
            .Where(message => message.ProcessedAt == null && message.NextAttemptAt <= asOf)
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .Take(take)
            .ToListAsync(cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
