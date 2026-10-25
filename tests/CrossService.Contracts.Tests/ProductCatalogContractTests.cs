using System.Net.Http.Json;
using InventorySystem.Products.Application.Dtos;
using InventorySystem.Stock.Application.Exceptions;
using InventorySystem.Stock.Infrastructure.Catalog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace InventorySystem.CrossService.Tests;

/// <summary>
/// Verifies the Inventory → Product HTTP contract with the real client against
/// the real Product API (hosted in-process). This is the seam most likely to
/// break silently, because the two services only agree on JSON field names.
/// </summary>
public class ProductCatalogContractTests
{
    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>Hosts Product.Api on a throwaway SQLite database.</summary>
    private sealed class ProductServiceFactory : WebApplicationFactory<Program>
    {
        private readonly string _databasePath =
            Path.Combine(Path.GetTempPath(), $"product-contract-{Guid.NewGuid():N}.db");

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");

            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "Sqlite",
                    ["ConnectionStrings:ProductDb"] = $"Data Source={_databasePath}"
                });
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
            {
                return;
            }

            try
            {
                if (File.Exists(_databasePath))
                {
                    File.Delete(_databasePath);
                }
            }
            catch (IOException)
            {
                // Still held by a pooled connection; a leftover temp file is harmless.
            }
        }
    }

    [Fact]
    public async Task The_inventory_client_reads_real_product_data()
    {
        using var productService = new ProductServiceFactory();
        var productClient = productService.CreateClient();
        var catalog = new HttpProductCatalogClient(productClient);

        var created = await (await productClient.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Sku = "contract-1",
            Name = "Contract Product",
            UnitPrice = 42.5m,
            MinimumStockLevel = 17
        }, Ct)).Content.ReadFromJsonAsync<ProductDto>(Ct);

        var product = await catalog.GetProductAsync(created!.Id, Ct);

        Assert.NotNull(product);
        Assert.Equal(created.Id, product!.Id);
        Assert.Equal("CONTRACT-1", product.Sku);      // normalised by the Product service
        Assert.Equal("Contract Product", product.Name);
        Assert.Equal(17, product.MinimumStockLevel);  // the value the Alert service depends on
        Assert.True(product.IsActive);
    }

    [Fact]
    public async Task The_inventory_client_reads_real_warehouse_data()
    {
        using var productService = new ProductServiceFactory();
        var productClient = productService.CreateClient();
        var catalog = new HttpProductCatalogClient(productClient);

        var created = await (await productClient.PostAsJsonAsync("/api/warehouses", new CreateWarehouseRequest
        {
            Code = "contract-wh",
            Name = "Contract Warehouse",
            Address = "Nowhere"
        }, Ct)).Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        var warehouse = await catalog.GetWarehouseAsync(created!.Id, Ct);

        Assert.NotNull(warehouse);
        Assert.Equal(created.Id, warehouse!.Id);
        Assert.Equal("CONTRACT-WH", warehouse.Code);
        Assert.Equal("Contract Warehouse", warehouse.Name);
        Assert.True(warehouse.IsActive);
    }

    [Fact]
    public async Task A_missing_product_or_warehouse_is_reported_as_not_found()
    {
        using var productService = new ProductServiceFactory();
        var catalog = new HttpProductCatalogClient(productService.CreateClient());

        Assert.Null(await catalog.GetProductAsync(4242, Ct));
        Assert.Null(await catalog.GetWarehouseAsync(4242, Ct));
    }

    [Fact]
    public async Task An_unreachable_product_service_surfaces_as_a_dependency_failure()
    {
        using var unreachable = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:1/"),
            Timeout = TimeSpan.FromSeconds(3)
        };

        var catalog = new HttpProductCatalogClient(unreachable);

        await Assert.ThrowsAsync<CatalogUnavailableException>(() => catalog.GetProductAsync(1, Ct));
    }
}
