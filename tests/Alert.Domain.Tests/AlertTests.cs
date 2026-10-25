using InventorySystem.Alerts.Domain.Entities;
using InventorySystem.Alerts.Domain.Exceptions;

namespace InventorySystem.Alerts.Domain.Tests;

/// <summary>
/// The Alert entity's rules (TASK-045): what a valid alert is, how a newer
/// observation updates it, and when it is resolved.
/// </summary>
public class AlertTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid EventId = Guid.Parse("6f9619ff-8b86-d011-b42d-00cf4fc964ff");

    private static Alert LowStockAlert(int quantity = 4, int minimum = 10) => Alert.Raise(
        productId: 1,
        warehouseId: 2,
        AlertType.LOW_STOCK,
        "Product 1 is low on stock in warehouse 2.",
        quantity,
        minimum,
        EventId,
        Now);

    [Fact]
    public void Raise_starts_an_active_unread_alert()
    {
        var alert = LowStockAlert();

        Assert.Equal(1, alert.ProductId);
        Assert.Equal(2, alert.WarehouseId);
        Assert.Equal(AlertType.LOW_STOCK, alert.Type);
        Assert.Equal(4, alert.CurrentQuantity);
        Assert.Equal(10, alert.MinimumStockLevel);
        Assert.False(alert.IsRead);
        Assert.Null(alert.ReadAt);
        Assert.Null(alert.ResolvedAt);
        Assert.Equal(AlertStatus.ACTIVE, alert.Status);
        Assert.True(alert.IsActive);
        Assert.Equal(Now, alert.CreatedAt);
        Assert.Equal(Now, alert.UpdatedAt);
        Assert.Equal(EventId, alert.SourceEventId);
    }

    [Theory]
    [InlineData(0, 1, 5, 10)]
    [InlineData(1, 0, 5, 10)]
    [InlineData(1, 1, -1, 10)]
    [InlineData(1, 1, 5, -10)]
    public void Raise_rejects_invalid_ids_and_negative_numbers(int productId, int warehouseId, int quantity, int minimum)
    {
        Assert.Throws<DomainException>(() => Alert.Raise(
            productId, warehouseId, AlertType.LOW_STOCK, "message", quantity, minimum, EventId, Now));
    }

    [Fact]
    public void Raise_requires_a_message_and_a_source_event()
    {
        Assert.Throws<DomainException>(() => Alert.Raise(1, 1, AlertType.LOW_STOCK, "   ", 1, 10, EventId, Now));
        Assert.Throws<DomainException>(() => Alert.Raise(1, 1, AlertType.LOW_STOCK, new string('x', Alert.MessageMaxLength + 1), 1, 10, EventId, Now));
        Assert.Throws<DomainException>(() => Alert.Raise(1, 1, AlertType.LOW_STOCK, "fine", 1, 10, Guid.Empty, Now));
    }

    [Fact]
    public void Raise_refuses_a_quantity_above_the_minimum()
    {
        var exception = Assert.Throws<DomainException>(() => Alert.Raise(
            1, 1, AlertType.LOW_STOCK, "message", 11, 10, EventId, Now));

        Assert.Contains("at or below the minimum", exception.Message);
    }

    [Fact]
    public void Raise_trims_the_message()
    {
        var alert = Alert.Raise(1, 1, AlertType.LOW_STOCK, "  spaced out  ", 1, 10, EventId, Now);

        Assert.Equal("spaced out", alert.Message);
    }

    [Fact]
    public void Refresh_brings_the_alert_up_to_date_without_readding_it()
    {
        var alert = LowStockAlert();
        alert.MarkAsRead(Now);

        var later = Now.AddMinutes(5);
        var newEvent = Guid.NewGuid();

        alert.Refresh(AlertType.OUT_OF_STOCK, "Product 1 is out of stock in warehouse 2.", 0, 10, newEvent, later);

        Assert.Equal(AlertType.OUT_OF_STOCK, alert.Type);
        Assert.Equal("Product 1 is out of stock in warehouse 2.", alert.Message);
        Assert.Equal(0, alert.CurrentQuantity);
        Assert.Equal(later, alert.UpdatedAt);
        Assert.Equal(newEvent, alert.SourceEventId);

        // The user already saw this alert: reading it twice must not reset that.
        Assert.True(alert.IsRead);
        Assert.Equal(Now, alert.ReadAt);
        Assert.True(alert.IsActive);
    }

    [Fact]
    public void Refresh_rejects_invalid_input_and_a_resolved_alert()
    {
        var alert = LowStockAlert();

        Assert.Throws<DomainException>(() => alert.Refresh(AlertType.LOW_STOCK, "  ", 1, 10, Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => alert.Refresh(AlertType.LOW_STOCK, new string('x', Alert.MessageMaxLength + 1), 1, 10, Guid.NewGuid(), Now));
        Assert.Throws<DomainException>(() => alert.Refresh(AlertType.LOW_STOCK, "message", -1, 10, Guid.NewGuid(), Now));

        alert.Resolve(Now, Guid.NewGuid());

        var exception = Assert.Throws<DomainException>(() => alert.Refresh(AlertType.LOW_STOCK, "message", 1, 10, Guid.NewGuid(), Now));

        Assert.Contains("resolved alert cannot be refreshed", exception.Message);
    }

    [Fact]
    public void MarkAsRead_is_idempotent_and_keeps_the_first_timestamp()
    {
        var alert = LowStockAlert();

        Assert.True(alert.MarkAsRead(Now));
        Assert.True(alert.IsRead);
        Assert.Equal(Now, alert.ReadAt);

        Assert.False(alert.MarkAsRead(Now.AddHours(1)));
        Assert.Equal(Now, alert.ReadAt);
    }

    [Fact]
    public void Resolve_closes_the_alert_and_ignores_a_second_call()
    {
        var alert = LowStockAlert();
        var resolvingEvent = Guid.NewGuid();
        var resolvedAt = Now.AddMinutes(1);

        alert.Resolve(resolvedAt, resolvingEvent);

        Assert.Equal(AlertStatus.RESOLVED, alert.Status);
        Assert.False(alert.IsActive);
        Assert.Equal(resolvedAt, alert.ResolvedAt);
        Assert.Equal(resolvedAt, alert.UpdatedAt);
        Assert.Equal(resolvingEvent, alert.SourceEventId);

        // Resolving again (a duplicate delivery, say) must not move the timestamps.
        alert.Resolve(resolvedAt.AddHours(1), Guid.NewGuid());

        Assert.Equal(resolvedAt, alert.ResolvedAt);
    }
}
