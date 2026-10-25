using System.Net.Http.Json;
using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Stock.Api.Tests;

/// <summary>
/// The over-issue rule ("requested quantity does not exceed available quantity")
/// has to hold when two operators act at the same moment, not just sequentially.
/// The stock row carries an optimistic-concurrency token so the second writer
/// fails instead of silently applying a decision made against stale stock.
/// </summary>
public class InventoryConcurrencyTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task Two_issues_cannot_both_consume_the_same_units()
    {
        using var factory = new InventoryApiFactory();
        factory.Catalog.WithProduct(1, minimumStockLevel: 10).WithWarehouse(1);
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 10 }, Ct);

        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseSqlite($"Data Source={factory.DatabasePath}")
            .Options;

        await using var firstOperator = new InventoryDbContext(options);
        await using var secondOperator = new InventoryDbContext(options);

        // Both operators load the same stock row: each believes 10 units are on hand.
        var firstRow = await firstOperator.StockLevels.SingleAsync(Ct);
        var secondRow = await secondOperator.StockLevels.SingleAsync(Ct);

        Assert.Equal(10, firstRow.Quantity);
        Assert.Equal(10, secondRow.Quantity);

        // Let the clock move past the stored UpdatedAt so the concurrency token
        // can distinguish the first write from the row the second operator read.
        await Task.Delay(50);

        firstRow.Issue(8);
        await firstOperator.SaveChangesAsync(Ct);

        secondRow.Issue(8);   // would over-issue if it were allowed to apply

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondOperator.SaveChangesAsync(Ct));

        await using var verify = new InventoryDbContext(options);
        var row = await verify.StockLevels.SingleAsync(Ct);

        // Exactly one issue was applied: 10 - 8, never -6.
        Assert.Equal(2, row.Quantity);
    }
}
