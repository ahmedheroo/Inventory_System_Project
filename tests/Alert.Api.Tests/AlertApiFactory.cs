using InventorySystem.Alerts.Api.Tests.Fakes;
using InventorySystem.Alerts.Application.Services;
using InventorySystem.Alerts.Domain.Entities;
using InventorySystem.Alerts.Infrastructure.Persistence;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Messaging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace InventorySystem.Alerts.Api.Tests;

/// <summary>
/// Boots the real Alert API on a throwaway SQLite database. The broker hop is the
/// only thing replaced: <see cref="IEventConsumer"/> is swapped for an in-memory
/// feed, so the real background consumer, the real low-stock projection and the
/// real database run exactly as they do in production.
/// </summary>
public class AlertApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"alerts-tests-{Guid.NewGuid():N}.db");

    /// <summary>Feed used to hand inventory events to the running consumer.</summary>
    public TestEventConsumer Feed { get; } = new();

    public string DatabasePath => _databasePath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:AlertDb"] = $"Data Source={_databasePath}",
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
        });
    }

    /// <summary>Applies an event through the real processor (deterministic, no feed).</summary>
    public async Task<AlertProcessingResult> ProcessAsync(IntegrationEvent integrationEvent)
    {
        await using var scope = Services.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<InventoryEventProcessor>();

        return await processor.ProcessAsync(integrationEvent);
    }

    public IReadOnlyList<Alert> StoredAlerts()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AlertDbContext>();

        return dbContext.Alerts
            .AsNoTracking()
            .OrderBy(alert => alert.Id)
            .ToList();
    }

    public int ProcessedEventCount()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AlertDbContext>();

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
