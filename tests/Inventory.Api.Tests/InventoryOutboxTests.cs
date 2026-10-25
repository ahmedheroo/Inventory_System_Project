using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Infrastructure.Messaging;
using InventorySystem.Stock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Stock.Api.Tests;

/// <summary>
/// The publishing guarantee of Sprint 4 (TASK-035 → TASK-043): a stock change and
/// its event are written by one transaction, and the event is delivered from the
/// outbox afterwards — so a broker that is down, slow or restarted cannot lose an
/// event, and cannot fail a stock operation either.
/// </summary>
public class InventoryOutboxTests
{
    private static CancellationToken Ct => CancellationToken.None;

    private static InventoryApiFactory CreateFactory()
    {
        var factory = new InventoryApiFactory();

        factory.Catalog
            .WithProduct(1, minimumStockLevel: 10)
            .WithWarehouse(1)
            .WithWarehouse(2);

        return factory;
    }

    private static Task<HttpResponseMessage> ReceiveAsync(HttpClient client, int quantity, int warehouseId = 1) =>
        client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = warehouseId, Quantity = quantity }, Ct);

    [Fact]
    public async Task A_stock_change_commits_its_event_in_the_same_transaction()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await ReceiveAsync(client, 20);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var message = Assert.Single(factory.OutboxMessages());

        Assert.Equal(StockReceivedEvent.Name, message.EventName);
        Assert.Null(message.ProcessedAt);                 // waiting for the dispatcher
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.LastError);

        // The stored payload round-trips into the exact event a consumer expects.
        var stored = Assert.IsType<StockReceivedEvent>(message.ToIntegrationEvent());

        Assert.Equal(1, stored.ProductId);
        Assert.Equal(1, stored.WarehouseId);
        Assert.Equal(20, stored.Quantity);
        Assert.Equal(20, stored.CurrentQuantity);
        Assert.Equal(10, stored.MinimumStockLevel);
        Assert.Equal(message.Id, stored.EventId);         // same id a consumer de-duplicates on

        // Nothing was published during the request: the API never talks to the bus.
        Assert.Empty(factory.Publisher.Events);
        Assert.Equal(0, factory.Publisher.Attempts);
    }

    [Fact]
    public async Task A_rejected_operation_commits_no_event()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var refused = await client.PostAsJsonAsync("/api/inventory/issue",
            new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 5 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Empty(factory.EnqueuedEvents());
    }

    [Fact]
    public async Task A_conflicting_write_rolls_back_its_event_with_the_stock_change()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await ReceiveAsync(client, 10);

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={factory.DatabasePath}")
            .Options;

        await using var firstOperator = new InventoryDbContext(options);
        await using var secondOperator = new InventoryDbContext(options);

        var firstRow = await firstOperator.StockLevels.SingleAsync(Ct);
        var secondRow = await secondOperator.StockLevels.SingleAsync(Ct);

        await Task.Delay(50);   // let the concurrency token differ between the writes

        var firstEvent = NewIssuedEvent(8);
        var secondEvent = NewIssuedEvent(8);

        firstRow.Issue(8);
        new OutboxStore(firstOperator).Enqueue(firstEvent);
        await firstOperator.SaveChangesAsync(Ct);

        secondRow.Issue(8);
        new OutboxStore(secondOperator).Enqueue(secondEvent);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondOperator.SaveChangesAsync(Ct));

        // The losing write left neither a stock change nor an event behind: exactly
        // one issued event exists, and the stock is 10 - 8.
        var issued = Assert.Single(factory.EnqueuedEvents().OfType<StockIssuedEvent>());

        Assert.Equal(firstEvent.EventId, issued.EventId);
        Assert.Equal(2, factory.PendingMessages().Count);   // the receive + the winning issue

        await using var verify = new InventoryDbContext(options);
        Assert.Equal(2, (await verify.StockLevels.SingleAsync(Ct)).Quantity);
    }

    [Fact]
    public async Task The_dispatcher_publishes_pending_events_and_marks_them_processed()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await ReceiveAsync(client, 20);
        await client.PostAsJsonAsync("/api/inventory/issue",
            new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 5 }, Ct);

        var result = await DispatchAsync(factory);

        Assert.Equal(2, result.Pending);
        Assert.Equal(2, result.Dispatched);
        Assert.Equal(0, result.Failed);

        var issued = factory.Publisher.Single<StockIssuedEvent>();

        Assert.Equal(15, issued.CurrentQuantity);

        Assert.Empty(factory.PendingMessages());
        Assert.All(factory.OutboxMessages(), message =>
        {
            Assert.NotNull(message.ProcessedAt);
            Assert.Equal(0, message.Attempts);
        });

        // Nothing left to do on the next pass.
        var second = await DispatchAsync(factory);

        Assert.Equal(0, second.Pending);
        Assert.Equal(2, factory.Publisher.Events.Count);   // not published twice
    }

    [Fact]
    public async Task A_broker_outage_keeps_events_in_the_outbox_and_delivers_them_afterwards()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await ReceiveAsync(client, 7);

        // The broker is down: the stock change still succeeds...
        factory.Publisher.Failure = new InvalidOperationException("Connection refused (broker down)");

        var failed = await DispatchAsync(factory);

        Assert.Equal(1, failed.Pending);
        Assert.Equal(0, failed.Dispatched);
        Assert.Equal(1, failed.Failed);

        // ...and the event is still there, with the failure recorded for operators.
        var pending = Assert.Single(factory.PendingMessages());

        Assert.Equal(1, pending.Attempts);
        Assert.Contains("broker down", pending.LastError);
        Assert.Empty(factory.Publisher.Events);

        // The broker comes back: the same event (same id, same payload) is delivered.
        factory.Publisher.Failure = null;

        var recovered = await DispatchAsync(factory);

        Assert.Equal(1, recovered.Dispatched);
        Assert.Empty(factory.PendingMessages());

        var delivered = Assert.Single(factory.Publisher.Events);

        Assert.Equal(pending.Id, delivered.EventId);
        Assert.Equal(StockReceivedEvent.Name, delivered.EventName);
        Assert.Equal(7, ((StockReceivedEvent)delivered).CurrentQuantity);
    }

    [Fact]
    public async Task The_dispatcher_publishes_in_the_order_the_events_happened()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await ReceiveAsync(client, 20);
        await client.PostAsJsonAsync("/api/inventory/issue",
            new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 4 }, Ct);
        await client.PostAsJsonAsync("/api/inventory/adjust",
            new AdjustStockRequest { ProductId = 1, WarehouseId = 1, NewQuantity = 14 }, Ct);

        await DispatchAsync(factory);

        Assert.Equal(
            new[] { StockReceivedEvent.Name, StockIssuedEvent.Name, StockAdjustedEvent.Name },
            factory.Publisher.Events.Select(integrationEvent => integrationEvent.EventName));
    }

    [Fact]
    public async Task Events_without_a_broker_stay_queued_instead_of_being_dropped()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await ReceiveAsync(client, 3);

        // "RabbitMq:Enabled=false" is what the test host runs with: the dispatcher
        // never starts, so the event must still be waiting (not lost).
        Assert.Single(factory.PendingMessages());
        Assert.Empty(factory.Publisher.Events);
    }

    private static Task<OutboxDispatchResult> DispatchAsync(InventoryApiFactory factory) =>
        factory.Services.GetRequiredService<OutboxProcessor>().ProcessPendingAsync(Ct);

    private static StockIssuedEvent NewIssuedEvent(int quantity) => new()
    {
        ProductId = 1,
        WarehouseId = 1,
        Quantity = quantity,
        CurrentQuantity = 2,
        MinimumStockLevel = 10
    };
}

/// <summary>The backoff rule the dispatcher applies between attempts.</summary>
public class OutboxBackoffTests
{
    private static readonly OutboxOptions Options = new()
    {
        InitialRetrySeconds = 2,
        MaxRetrySeconds = 60
    };

    [Fact]
    public void Backoff_grows_exponentially_and_is_capped()
    {
        Assert.Equal(TimeSpan.FromSeconds(2), Options.BackoffFor(1));
        Assert.Equal(TimeSpan.FromSeconds(4), Options.BackoffFor(2));
        Assert.Equal(TimeSpan.FromSeconds(8), Options.BackoffFor(3));
        Assert.Equal(TimeSpan.FromSeconds(60), Options.BackoffFor(10));
        Assert.Equal(TimeSpan.FromSeconds(60), Options.BackoffFor(1_000));
    }

    [Fact]
    public void Backoff_treats_a_non_positive_attempt_count_as_the_first_attempt()
    {
        Assert.Equal(Options.BackoffFor(1), Options.BackoffFor(0));
    }
}

/// <summary>
/// The outbox port is what makes the guarantee hold, so its contract with the
/// transaction is asserted directly as well.
/// </summary>
public class OutboxStoreContractTests
{
    [Fact]
    public async Task Enqueue_does_not_touch_the_database_until_the_unit_of_work_commits()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"outbox-contract-{Guid.NewGuid():N}.db");

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;

        try
        {
            await using (var setup = new InventoryDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync();
            }

            await using var dbContext = new InventoryDbContext(options);

            IEventOutbox outbox = new OutboxStore(dbContext);
            outbox.Enqueue(new StockReceivedEvent { ProductId = 1, WarehouseId = 1, Quantity = 1 });

            await using (var other = new InventoryDbContext(options))
            {
                Assert.Empty(await other.OutboxMessages.AsNoTracking().ToListAsync());
            }

            await dbContext.SaveChangesAsync();

            await using (var other = new InventoryDbContext(options))
            {
                Assert.Single(await other.OutboxMessages.AsNoTracking().ToListAsync());
            }
        }
        finally
        {
            try
            {
                if (File.Exists(databasePath))
                {
                    File.Delete(databasePath);
                }
            }
            catch (IOException)
            {
                // Harmless leftover temp file.
            }
        }
    }
}
