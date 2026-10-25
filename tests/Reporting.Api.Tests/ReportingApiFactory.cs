using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Messaging;
using InventorySystem.Reports.Api.Tests.Fakes;
using InventorySystem.Reports.Application.Abstractions;
using InventorySystem.Reports.Application.ReadModel;
using InventorySystem.Reports.Application.Services;
using InventorySystem.Reports.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace InventorySystem.Reports.Api.Tests;

/// <summary>
/// Boots the real Reporting API on a throwaway SQLite database. The two external
/// collaborators are the only things replaced: the broker by an in-memory feed
/// (so the real background consumer, the real projector and the real database all
/// run) and the Product service by an inspecting fake.
/// </summary>
public class ReportingApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"reporting-tests-{Guid.NewGuid():N}.db");

    /// <summary>Feed used to hand inventory events to the running consumer.</summary>
    public TestEventConsumer Feed { get; } = new();

    /// <summary>Stand-in for the Product service (sku, names, unit prices).</summary>
    public FakeProductCatalogClient Catalog { get; } = new();

    public string DatabasePath => _databasePath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:ReportingDb"] = $"Data Source={_databasePath}",
                // Never called: the catalogue client is replaced below.
                ["Services:ProductApi:BaseUrl"] = "http://localhost:5251",
                // Enabled on purpose: the consumer hosted service has to run so the
                // tests exercise the real consuming path. It talks to the feed below,
                // never to a broker.
                ["RabbitMq:Enabled"] = "true"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEventConsumer>();
            services.AddSingleton<IEventConsumer>(Feed);

            services.RemoveAll<IProductCatalogClient>();
            services.AddSingleton<IProductCatalogClient>(Catalog);
        });
    }

    /// <summary>Applies an event through the real projector (deterministic, no feed).</summary>
    public async Task<ProjectionResult> ProjectAsync(IntegrationEvent integrationEvent)
    {
        await using var scope = Services.CreateAsyncScope();
        var projector = scope.ServiceProvider.GetRequiredService<InventoryEventProjector>();

        return await projector.ProjectAsync(integrationEvent);
    }

    /// <summary>The projected stock levels, one row per (product, warehouse) pair.</summary>
    public IReadOnlyList<StockLevelSnapshot> StockLevels()
    {
        using var scope = Services.CreateScope();

        return scope.ServiceProvider
            .GetRequiredService<ReportingDbContext>()
            .StockLevels
            .AsNoTracking()
            .OrderBy(stockLevel => stockLevel.ProductId)
            .ThenBy(stockLevel => stockLevel.WarehouseId)
            .ToList();
    }

    /// <summary>The movement ledger, oldest first.</summary>
    public IReadOnlyList<StockMovement> Movements()
    {
        using var scope = Services.CreateScope();

        return scope.ServiceProvider
            .GetRequiredService<ReportingDbContext>()
            .StockMovements
            .AsNoTracking()
            .OrderBy(movement => movement.Id)
            .ToList();
    }

    public int ProcessedEventCount()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ReportingDbContext>();

        return dbContext.ProcessedEvents.AsNoTracking().Count();
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

/// <summary>Polling helper: the consumer runs on its own loop, so tests wait for the effect.</summary>
public static class Wait
{
    public static async Task UntilAsync(Func<bool> condition, string because, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new Xunit.Sdk.XunitException($"Timed out waiting for: {because}");
    }
}
