using InventorySystem.Stock.Domain.Entities;
using InventorySystem.Stock.Domain.Exceptions;

namespace InventorySystem.Stock.Domain.Tests;

/// <summary>
/// The specification's inventory business rules (section 7.2):
/// quantity cannot be negative, and an issue may not exceed what is available.
/// </summary>
public class InventoryTests
{
    [Fact]
    public void Create_starts_at_the_given_quantity()
    {
        var inventory = Inventory.Create(1, 2, 7);

        Assert.Equal(1, inventory.ProductId);
        Assert.Equal(2, inventory.WarehouseId);
        Assert.Equal(7, inventory.Quantity);
    }

    [Fact]
    public void Create_rejects_negative_initial_quantity()
    {
        Assert.Throws<DomainException>(() => Inventory.Create(1, 2, -1));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    [InlineData(1, 0)]
    public void Create_rejects_non_positive_ids(int productId, int warehouseId)
    {
        Assert.Throws<DomainException>(() => Inventory.Create(productId, warehouseId));
    }

    [Fact]
    public void Receive_adds_stock_and_returns_the_new_quantity()
    {
        var inventory = Inventory.Create(1, 1, 10);

        var newQuantity = inventory.Receive(5);

        Assert.Equal(15, newQuantity);
        Assert.Equal(15, inventory.Quantity);
    }

    [Fact]
    public void TransferIn_adds_stock()
    {
        var inventory = Inventory.Create(1, 1, 4);

        Assert.Equal(9, inventory.TransferIn(5));
    }

    [Fact]
    public void Issue_removes_stock_and_returns_the_new_quantity()
    {
        var inventory = Inventory.Create(1, 1, 10);

        var newQuantity = inventory.Issue(4);

        Assert.Equal(6, newQuantity);
        Assert.Equal(6, inventory.Quantity);
    }

    [Fact]
    public void Issue_may_empty_the_stock_but_never_below_zero()
    {
        var inventory = Inventory.Create(1, 1, 10);

        Assert.Equal(0, inventory.Issue(10));
        Assert.Equal(0, inventory.Quantity);

        Assert.Throws<DomainException>(() => inventory.Issue(1));
        Assert.Equal(0, inventory.Quantity);
    }

    [Fact]
    public void Issue_beyond_the_available_quantity_is_refused_and_leaves_stock_untouched()
    {
        var inventory = Inventory.Create(1, 1, 10);

        var exception = Assert.Throws<DomainException>(() => inventory.Issue(11));

        Assert.Contains("only 10 available", exception.Message);
        Assert.Equal(10, inventory.Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Issue_requires_a_positive_quantity(int quantity)
    {
        var inventory = Inventory.Create(1, 1, 10);

        var exception = Assert.Throws<DomainException>(() => inventory.Issue(quantity));

        Assert.Contains("greater than zero", exception.Message);
        Assert.Equal(10, inventory.Quantity);
    }

    [Fact]
    public void TransferOut_obeys_the_same_rule_as_an_issue()
    {
        var inventory = Inventory.Create(1, 1, 3);

        Assert.Equal(0, inventory.TransferOut(3));

        var exception = Assert.Throws<DomainException>(() => inventory.TransferOut(1));
        Assert.Contains("transfer out", exception.Message);
    }

    [Theory]
    [InlineData(10, 18, 8)]   // increase
    [InlineData(10, 4, -6)]   // decrease
    [InlineData(10, 10, 0)]   // no change
    public void Adjust_sets_an_absolute_quantity_and_reports_the_delta(int start, int target, int expectedDelta)
    {
        var inventory = Inventory.Create(1, 1, start);

        var delta = inventory.Adjust(target);

        Assert.Equal(expectedDelta, delta);
        Assert.Equal(target, inventory.Quantity);
    }

    [Fact]
    public void Adjust_cannot_produce_a_negative_quantity()
    {
        var inventory = Inventory.Create(1, 1, 10);

        Assert.Throws<DomainException>(() => inventory.Adjust(-1));
        Assert.Equal(10, inventory.Quantity);
    }

    [Fact]
    public void Every_modification_moves_updated_at_forward()
    {
        var inventory = Inventory.Create(1, 1, 10);
        var created = inventory.UpdatedAt;

        inventory.Receive(1);

        Assert.True(inventory.UpdatedAt >= created);
    }
}
