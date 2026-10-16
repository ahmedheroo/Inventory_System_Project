using InventorySystem.Products.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Products.Infrastructure.Persistence;

public static class ProductDbInitializer
{
    /// <summary>
    /// Applies migrations (SQL Server) or creates the schema directly (SQLite dev fallback).
    /// </summary>
    public static async Task InitializeAsync(
        ProductDbContext dbContext,
        ProductDatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (options.ApplyMigrations)
        {
            logger.LogInformation("Applying ProductDb migrations ({Provider})...", options.Provider);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Ensuring ProductDb schema exists ({Provider} dev fallback)...", options.Provider);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        logger.LogInformation("ProductDb is ready.");
    }

    /// <summary>Seeds a small catalogue so a fresh environment is usable immediately.</summary>
    public static async Task SeedAsync(ProductDbContext dbContext, CancellationToken cancellationToken = default)
    {
        if (await dbContext.Categories.AnyAsync(cancellationToken) ||
            await dbContext.Products.AnyAsync(cancellationToken))
        {
            return;
        }

        var electronics = Category.Create("Electronics", "Devices and accessories");
        var groceries = Category.Create("Groceries", "Consumable goods");
        var stationery = Category.Create("Stationery", "Office supplies");

        dbContext.Categories.AddRange(electronics, groceries, stationery);

        dbContext.Warehouses.AddRange(
            Warehouse.Create("WH-CAI", "Cairo Main Warehouse", "Cairo, Egypt"),
            Warehouse.Create("WH-ALX", "Alexandria Warehouse", "Alexandria, Egypt"));

        // Save first so the seeded categories receive their identity values.
        await dbContext.SaveChangesAsync(cancellationToken);

        dbContext.Products.AddRange(
            Product.Create("SKU-1001", "Wireless Mouse", "2.4 GHz optical mouse", electronics.Id, 350m, 10),
            Product.Create("SKU-1002", "Mechanical Keyboard", "RGB backlit keyboard", electronics.Id, 1200m, 5),
            Product.Create("SKU-2001", "Coffee Beans 1kg", "Medium roast arabica", groceries.Id, 480m, 20),
            Product.Create("SKU-3001", "A4 Paper Ream", "500 sheets, 80gsm", stationery.Id, 95m, 30));

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
