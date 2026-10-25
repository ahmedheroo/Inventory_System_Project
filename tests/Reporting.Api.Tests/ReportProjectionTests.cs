using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Reports.Application.ReadModel;

namespace InventorySystem.Reports.Api.Tests;

/// <summary>
/// TASK-057: the four stock events projected into the reporting read model. Every
/// case runs through the real projector and the real database; only the broker is
/// replaced (by the in-memory feed in <see cref="ReportingApiFactory"/>).
/// </summary>
public class ReportProjectionTests
{
    [Fact]
    public async Task A_receive_event_creates_the_stock_level_and_one_movement()
    {
        using var factory = new ReportingApiFactory();

        var result = await factory.ProjectAsync(TestEvents.Received(1, 1, 10, 10, TestEvents.At(0), minimumStockLevel: 5));

        Assert.False(result.Duplicate);
        Assert.Equal(1, result.StockLevels);
        Assert.Equal(1, result.Movements);

        var stockLevel = Assert.Single(factory.StockLevels());

        Assert.Equal(1, stockLevel.ProductId);
        Assert.Equal(1, stockLevel.WarehouseId);
        Assert.Equal(10, stockLevel.Quantity);
        Assert.Equal(5, stockLevel.MinimumStockLevel);
        Assert.False(stockLevel.IsLowStock);

        var movement = Assert.Single(factory.Movements());

        Assert.Equal(StockTransactionType.RECEIVE, movement.Type);
        Assert.Equal(10, movement.Quantity);
        Assert.Equal(10, movement.SignedQuantity);
        Assert.Equal(10, movement.QuantityAfter);
        Assert.Equal(TestEvents.At(0), movement.OccurredAt);
    }

    [Fact]
    public async Task A_transfer_becomes_two_movements_and_two_stock_levels()
    {
        using var factory = new ReportingApiFactory();

        var result = await factory.ProjectAsync(
            TestEvents.Transferred(7, 1, 2, quantity: 4, sourceCurrentQuantity: 6, destinationCurrentQuantity: 4, TestEvents.At(0)));

        Assert.Equal(2, result.StockLevels);
        Assert.Equal(2, result.Movements);

        var stockLevels = factory.StockLevels();

        Assert.Equal(6, stockLevels.Single(level => level.WarehouseId == 1).Quantity);
        Assert.Equal(4, stockLevels.Single(level => level.WarehouseId == 2).Quantity);

        var movements = factory.Movements();

        var outbound = movements.Single(movement => movement.Type == StockTransactionType.TRANSFER_OUT);
        var inbound = movements.Single(movement => movement.Type == StockTransactionType.TRANSFER_IN);

        Assert.Equal(1, outbound.WarehouseId);
        Assert.Equal(-4, outbound.SignedQuantity);
        Assert.Equal(6, outbound.QuantityAfter);

        Assert.Equal(2, inbound.WarehouseId);
        Assert.Equal(4, inbound.SignedQuantity);
        Assert.Equal(4, inbound.QuantityAfter);
    }

    [Fact]
    public async Task An_adjustment_records_the_signed_delta_and_the_counted_quantity()
    {
        using var factory = new ReportingApiFactory();

        await factory.ProjectAsync(TestEvents.Adjusted(3, 1, oldQuantity: 10, newQuantity: 7, TestEvents.At(0)));

        var movement = Assert.Single(factory.Movements());

        Assert.Equal(StockTransactionType.ADJUSTMENT, movement.Type);
        Assert.Equal(-3, movement.Quantity);
        Assert.Equal(-3, movement.SignedQuantity);
        Assert.Equal(7, movement.QuantityAfter);

        Assert.Equal(7, Assert.Single(factory.StockLevels()).Quantity);
    }

    [Fact]
    public async Task An_issue_event_moves_the_ledger_and_the_stock_level_down()
    {
        using var factory = new ReportingApiFactory();

        await factory.ProjectAsync(TestEvents.Received(1, 1, 10, 10, TestEvents.At(0)));
        await factory.ProjectAsync(TestEvents.Issued(1, 1, 4, 6, TestEvents.At(1), minimumStockLevel: 8));

        var stockLevel = Assert.Single(factory.StockLevels());

        Assert.Equal(6, stockLevel.Quantity);
        Assert.Equal(8, stockLevel.MinimumStockLevel);      // the newest event wins
        Assert.True(stockLevel.IsLowStock);                 // 6 <= 8

        Assert.Equal(2, factory.Movements().Count);         // the ledger is history, the level is current
        Assert.Equal(-4, factory.Movements().Last().SignedQuantity);
    }

    [Fact]
    public async Task A_redelivered_event_is_projected_once()
    {
        using var factory = new ReportingApiFactory();

        var received = TestEvents.Received(1, 1, 10, 10, TestEvents.At(0));

        await factory.ProjectAsync(received);

        var duplicate = await factory.ProjectAsync(received);

        Assert.True(duplicate.Duplicate);
        Assert.Equal(0, duplicate.Movements);

        Assert.Single(factory.Movements());
        Assert.Single(factory.StockLevels());
        Assert.Equal(1, factory.ProcessedEventCount());
    }

    [Fact]
    public async Task Events_for_different_pairs_project_separately()
    {
        using var factory = new ReportingApiFactory();

        await factory.ProjectAsync(TestEvents.Received(1, 1, 10, 10, TestEvents.At(0)));
        await factory.ProjectAsync(TestEvents.Received(2, 1, 5, 5, TestEvents.At(1)));
        await factory.ProjectAsync(TestEvents.Received(1, 2, 3, 3, TestEvents.At(2)));

        var stockLevels = factory.StockLevels();

        Assert.Equal(3, stockLevels.Count);
        Assert.Equal(3, factory.Movements().Count);
        Assert.Equal(3, factory.ProcessedEventCount());
    }

    [Fact]
    public async Task An_event_delivered_through_the_consumer_feed_reaches_the_read_model()
    {
        using var factory = new ReportingApiFactory();

        // Touch the host so the hosted consumer starts, exactly as it would in production.
        _ = factory.CreateClient();

        factory.Feed.Publish(TestEvents.Received(5, 3, 8, 8, TestEvents.At(0)));

        await Wait.UntilAsync(
            () => factory.StockLevels().Count == 1,
            "the consumer to project the published event");

        var stockLevel = Assert.Single(factory.StockLevels());

        Assert.Equal(5, stockLevel.ProductId);
        Assert.Equal(8, stockLevel.Quantity);
    }

    [Fact]
    public async Task The_consumer_binds_the_reporting_queue_to_every_stock_event()
    {
        using var factory = new ReportingApiFactory();

        _ = factory.CreateClient();

        // The binding is declared on the consuming loop, so give it a moment.
        await Wait.UntilAsync(() => factory.Feed.Queues.Count > 0, "the consumer to bind its queue");

        Assert.Contains("reporting.inventory", factory.Feed.Queues);
        Assert.Contains("stock.*", factory.Feed.RoutingKeys);
    }

    [Fact]
    public void The_read_model_rejects_a_negative_quantity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StockLevelSnapshot.Create(1, 1, quantity: -1, minimumStockLevel: 5, Guid.NewGuid(), DateTime.UtcNow));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StockMovement.Record(1, 1, StockTransactionType.RECEIVE, quantity: 0, quantityAfter: 0, Guid.NewGuid(), DateTime.UtcNow));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StockMovement.Record(1, 1, StockTransactionType.ADJUSTMENT, quantity: 0, quantityAfter: 4, Guid.NewGuid(), DateTime.UtcNow));
    }
}
