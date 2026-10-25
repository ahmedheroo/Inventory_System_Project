using System.Security.Claims;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Messaging;
using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Stock.Api.Tests.Fakes;
using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace InventorySystem.Stock.Api.Tests;

/// <summary>
/// Boots the real Inventory API on a throwaway SQLite database, with the two
/// external collaborators (Product service, message transport) replaced:
/// the catalogue by an inspecting fake, the broker by a recording publisher.
///
/// <c>RabbitMq:Enabled</c> is false, so the outbox dispatcher does not run on its
/// own — a test that wants a dispatch pass asks for one explicitly, which keeps
/// every assertion about the outbox deterministic.
/// </summary>
public class InventoryApiFactory : WebApplicationFactory<Program>
{
    /// <summary>
    /// The tokens this service is configured to accept. Deliberately not the development key the
    /// services ship with: a test that signs its own tokens proves the service validates what its
    /// configuration says rather than what it happens to be compiled against.
    /// </summary>
    public static readonly JwtOptions Tokens = new(
        "inventory-identity-tests",
        "inventory-api-tests",
        "inventory-tests-signing-key-long-enough-to-sign",
        TimeSpan.FromHours(1));

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"inventory-tests-{Guid.NewGuid():N}.db");

    public FakeProductCatalogClient Catalog { get; } = new();

    public RecordingEventPublisher Publisher { get; } = new();

    /// <summary>The live notifications the service would have broadcast to connected clients.</summary>
    public RecordingStockBroadcaster Broadcaster { get; } = new();

    /// <summary>
    /// When true the service keeps its real SignalR broadcaster, so a test can connect a client and
    /// watch a notification arrive over the wire. False by default, because most tests want to read
    /// what would have been sent without a connection in the way.
    /// </summary>
    public bool BroadcastThroughSignalR { get; init; }

    public string DatabasePath => _databasePath;

    /// <summary>
    /// A token this service will accept, carrying exactly the scopes asked for — the same shape the
    /// Identity service issues. The hub takes a <see cref="JwtScopes.StockRead"/> token; anything
    /// without that scope is what the door is there to refuse.
    /// </summary>
    public static string TokenFor(params string[] scopes)
    {
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, "1"),
            new(JwtClaimNames.Name, "console"),
            new(JwtClaimNames.Role, JwtRoles.Service)
        };

        claims.AddRange(scopes.Select(scope => new Claim(JwtClaimNames.Scope, scope)));

        return new JwtTokenFactory(Tokens).Issue(claims, DateTimeOffset.UtcNow).AccessToken;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:InventoryDb"] = $"Data Source={_databasePath}",
                // Never called: the catalog client is replaced below.
                ["Services:ProductApi:BaseUrl"] = "http://localhost:5251",
                // No broker in tests: the dispatcher stays idle unless driven.
                ["RabbitMq:Enabled"] = "false",
                ["Outbox:InitialRetrySeconds"] = "0",
                // The tokens this service accepts. Set here rather than left to the published
                // defaults, so a test token and the service cannot agree by accident.
                ["Jwt:Issuer"] = Tokens.Issuer,
                ["Jwt:Audience"] = Tokens.Audience,
                ["Jwt:SigningKey"] = Tokens.SigningKey,
                ["Jwt:LifetimeMinutes"] = "60"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IProductCatalogClient>();
            services.AddSingleton<IProductCatalogClient>(Catalog);

            services.RemoveAll<IEventPublisher>();
            services.AddSingleton<IEventPublisher>(Publisher);

            if (!BroadcastThroughSignalR)
            {
                services.RemoveAll<IStockBroadcaster>();
                services.AddSingleton<IStockBroadcaster>(Broadcaster);
            }
        });
    }

    /// <summary>Every outbox row, oldest first — the events the service committed.</summary>
    public IReadOnlyList<OutboxMessage> OutboxMessages()
    {
        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        return dbContext.OutboxMessages
            .AsNoTracking()
            .OrderBy(message => message.OccurredAt)
            .ThenBy(message => message.Id)
            .ToList();
    }

    /// <summary>The committed events, deserialized exactly as a consumer would.</summary>
    public IReadOnlyList<IntegrationEvent> EnqueuedEvents() =>
        OutboxMessages().Select(message => message.ToIntegrationEvent()).ToList();

    /// <summary>Events the dispatcher has not delivered yet.</summary>
    public IReadOnlyList<OutboxMessage> PendingMessages() =>
        OutboxMessages().Where(message => message.IsPending).ToList();

    public T SingleEvent<T>() where T : IntegrationEvent
    {
        var match = EnqueuedEvents().OfType<T>().ToList();

        Assert.Single(match);

        return match[0];
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
