using InventorySystem.Alerts.Domain.Entities;
using InventorySystem.Alerts.Domain.Rules;

namespace InventorySystem.Alerts.Domain.Tests;

/// <summary>
/// The low-stock rule from the specification (section 7.3):
/// an alert is raised when <c>CurrentQuantity &lt;= MinimumStockLevel</c>.
/// The boundaries are asserted explicitly because off-by-one here means either
/// missed alerts or alert noise.
/// </summary>
public class LowStockDetectorTests
{
    [Theory]
    [InlineData(11, 10, null)]                      // above the minimum
    [InlineData(10, 10, AlertType.LOW_STOCK)]       // exactly at the minimum (spec boundary)
    [InlineData(9, 10, AlertType.LOW_STOCK)]
    [InlineData(1, 10, AlertType.LOW_STOCK)]
    [InlineData(0, 10, AlertType.OUT_OF_STOCK)]     // nothing left
    [InlineData(0, 0, AlertType.OUT_OF_STOCK)]      // minimum 0 means "0 is out of stock"
    [InlineData(1, 0, null)]
    public void Detect_follows_the_at_or_below_threshold_rule(int quantity, int minimum, AlertType? expected)
    {
        Assert.Equal(expected, LowStockDetector.Detect(quantity, minimum));
        Assert.Equal(expected is not null, LowStockDetector.IsLowStock(quantity, minimum));
    }

    [Fact]
    public void Describe_mentions_the_product_warehouse_and_quantities()
    {
        var lowStock = LowStockDetector.Describe(7, 3, AlertType.LOW_STOCK, 4, 10);
        var outOfStock = LowStockDetector.Describe(7, 3, AlertType.OUT_OF_STOCK, 0, 10);

        Assert.Contains("7", lowStock);
        Assert.Contains("3", lowStock);
        Assert.Contains("4", lowStock);
        Assert.Contains("minimum stock level 10", lowStock);

        Assert.Contains("out of stock", outOfStock);
        Assert.Contains("minimum stock level 10", outOfStock);
    }

    [Fact]
    public void Describe_never_exceeds_the_message_limit()
    {
        var message = LowStockDetector.Describe(int.MaxValue, int.MaxValue, AlertType.LOW_STOCK, 1, int.MaxValue);

        Assert.True(message.Length <= Alert.MessageMaxLength);
    }
}
