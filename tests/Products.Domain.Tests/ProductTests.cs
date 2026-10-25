using InventorySystem.Products.Domain.Entities;
using InventorySystem.Products.Domain.Exceptions;

namespace InventorySystem.Products.Domain.Tests;

/// <summary>Domain rules behind the "Product Creation" acceptance criteria (spec section 18).</summary>
public class ProductTests
{
    [Fact]
    public void Create_normalizes_sku_to_uppercase_and_trims_input()
    {
        var product = Product.Create("  sku-1 ", "  Wireless Mouse  ", " Adaptive ", null, 10.5m, 3);

        Assert.Equal("SKU-1", product.Sku);
        Assert.Equal("Wireless Mouse", product.Name);
        Assert.Equal("Adaptive", product.Description);
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Create_rounds_unit_price_to_two_decimals()
    {
        var product = Product.Create("SKU-1", "Mouse", null, null, 10.555m, 0);

        Assert.Equal(10.56m, product.UnitPrice);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_rejects_missing_sku(string sku)
    {
        var exception = Assert.Throws<DomainException>(() =>
            Product.Create(sku, "Mouse", null, null, 1m, 0));

        Assert.Contains("SKU", exception.Message);
    }

    [Fact]
    public void Create_rejects_missing_name()
    {
        var exception = Assert.Throws<DomainException>(() =>
            Product.Create("SKU-1", " ", null, null, 1m, 0));

        Assert.Contains("name", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_negative_unit_price()
    {
        Assert.Throws<DomainException>(() => Product.Create("SKU-1", "Mouse", null, null, -0.01m, 0));
    }

    [Fact]
    public void Create_rejects_negative_minimum_stock_level()
    {
        Assert.Throws<DomainException>(() => Product.Create("SKU-1", "Mouse", null, null, 1m, -1));
    }

    [Fact]
    public void Deactivate_and_activate_toggle_the_active_flag()
    {
        var product = Product.Create("SKU-1", "Mouse", null, null, 1m, 0);

        product.Deactivate();
        Assert.False(product.IsActive);

        product.Activate();
        Assert.True(product.IsActive);
    }

    [Fact]
    public void Update_replaces_mutable_fields_and_stamps_updated_at()
    {
        var product = Product.Create("SKU-1", "Mouse", null, null, 10m, 1);
        var createdAt = product.CreatedAt;

        product.Update("Keyboard", "New", 7, 20m, 5);

        Assert.Equal("Keyboard", product.Name);
        Assert.Equal("New", product.Description);
        Assert.Equal(7, product.CategoryId);
        Assert.Equal(20m, product.UnitPrice);
        Assert.Equal(5, product.MinimumStockLevel);

        // CreatedAt is immutable; UpdatedAt is re-stamped at update time.
        Assert.Equal(createdAt, product.CreatedAt);
        Assert.True((DateTime.UtcNow - product.UpdatedAt).Duration() < TimeSpan.FromSeconds(5));
    }

    [Theory]
    [InlineData(10, 10, true)]  // equal to the minimum -> low stock
    [InlineData(9, 10, true)]   // below the minimum -> low stock
    [InlineData(11, 10, false)] // above the minimum -> fine
    [InlineData(0, 0, true)]    // zero minimum with no stock is still low
    public void IsLowStock_uses_the_specified_rule(int quantity, int minimum, bool expected)
    {
        var product = Product.Create("SKU-1", "Mouse", null, null, 1m, minimum);

        Assert.Equal(expected, product.IsLowStock(quantity));
    }

    [Fact]
    public void ChangeSku_normalizes_and_rejects_invalid_values()
    {
        var product = Product.Create("SKU-1", "Mouse", null, null, 1m, 0);

        product.ChangeSku(" sku-2 ");
        Assert.Equal("SKU-2", product.Sku);

        Assert.Throws<DomainException>(() => product.ChangeSku(""));
    }
}
