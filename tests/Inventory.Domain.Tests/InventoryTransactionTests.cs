using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Stock.Domain.Entities;
using InventorySystem.Stock.Domain.Exceptions;

namespace InventorySystem.Stock.Domain.Tests;

public class InventoryTransactionTests
{
    [Theory]
    [InlineData(StockTransactionType.RECEIVE, 5, 5)]
    [InlineData(StockTransactionType.TRANSFER_IN, 5, 5)]
    [InlineData(StockTransactionType.ISSUE, 5, -5)]
    [InlineData(StockTransactionType.TRANSFER_OUT, 5, -5)]
    [InlineData(StockTransactionType.ADJUSTMENT, -3, -3)]
    [InlineData(StockTransactionType.ADJUSTMENT, 3, 3)]
    public void SignedQuantity_normalises_direction(StockTransactionType type, int quantity, int expected)
    {
        var transaction = InventoryTransaction.Create(1, 1, type, quantity);

        Assert.Equal(expected, transaction.SignedQuantity);
    }

    [Theory]
    [InlineData(StockTransactionType.RECEIVE)]
    [InlineData(StockTransactionType.ISSUE)]
    [InlineData(StockTransactionType.TRANSFER_IN)]
    [InlineData(StockTransactionType.TRANSFER_OUT)]
    public void Out_and_in_types_require_a_positive_quantity(StockTransactionType type)
    {
        Assert.Throws<DomainException>(() => InventoryTransaction.Create(1, 1, type, 0));
        Assert.Throws<DomainException>(() => InventoryTransaction.Create(1, 1, type, -1));
    }

    [Fact]
    public void Adjustment_must_record_a_non_zero_change()
    {
        Assert.Throws<DomainException>(() => InventoryTransaction.Create(1, 1, StockTransactionType.ADJUSTMENT, 0));
    }

    [Fact]
    public void Reference_is_trimmed_and_blank_becomes_null()
    {
        Assert.Equal("PO-1", InventoryTransaction.Create(1, 1, StockTransactionType.RECEIVE, 1, "  PO-1 ").Reference);
        Assert.Null(InventoryTransaction.Create(1, 1, StockTransactionType.RECEIVE, 1, "   ").Reference);
    }

    [Fact]
    public void Reference_longer_than_the_limit_is_refused()
    {
        var tooLong = new string('x', InventoryTransaction.ReferenceMaxLength + 1);

        Assert.Throws<DomainException>(() =>
            InventoryTransaction.Create(1, 1, StockTransactionType.RECEIVE, 1, tooLong));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void Ids_must_be_positive(int productId, int warehouseId)
    {
        Assert.Throws<DomainException>(() =>
            InventoryTransaction.Create(productId, warehouseId, StockTransactionType.RECEIVE, 1));
    }
}
