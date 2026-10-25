using InventorySystem.Products.Domain.Entities;
using InventorySystem.Products.Domain.Exceptions;

namespace InventorySystem.Products.Domain.Tests;

public class CategoryTests
{
    [Fact]
    public void Create_trims_and_keeps_description_optional()
    {
        var category = Category.Create("  Electronics ", "  Devices ");

        Assert.Equal("Electronics", category.Name);
        Assert.Equal("Devices", category.Description);
    }

    [Fact]
    public void Create_turns_blank_description_into_null()
    {
        Assert.Null(Category.Create("Electronics", "   ").Description);
    }

    [Fact]
    public void Create_rejects_missing_name()
    {
        Assert.Throws<DomainException>(() => Category.Create("   ", null));
    }

    [Fact]
    public void Create_rejects_names_longer_than_the_limit()
    {
        var tooLong = new string('a', Category.NameMaxLength + 1);

        Assert.Throws<DomainException>(() => Category.Create(tooLong, null));
    }
}

public class WarehouseTests
{
    [Fact]
    public void Create_normalizes_the_code()
    {
        var warehouse = Warehouse.Create(" wh-cai ", " Cairo Main ", " Cairo ");

        Assert.Equal("WH-CAI", warehouse.Code);
        Assert.Equal("Cairo Main", warehouse.Name);
        Assert.True(warehouse.IsActive);
    }

    [Fact]
    public void Create_rejects_missing_code()
    {
        var exception = Assert.Throws<DomainException>(() => Warehouse.Create(" ", "Cairo", null));

        Assert.Contains("code", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Create_rejects_missing_name()
    {
        Assert.Throws<DomainException>(() => Warehouse.Create("WH-1", "", null));
    }

    [Fact]
    public void Deactivate_marks_the_warehouse_inactive()
    {
        var warehouse = Warehouse.Create("WH-1", "Cairo", null);

        warehouse.Deactivate();

        Assert.False(warehouse.IsActive);

        warehouse.Activate();
        Assert.True(warehouse.IsActive);
    }
}
