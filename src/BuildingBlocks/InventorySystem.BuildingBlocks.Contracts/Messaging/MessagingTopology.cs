namespace InventorySystem.BuildingBlocks.Contracts.Messaging;

/// <summary>
/// RabbitMQ topology shared by publishers and consumers. Keeping the names here
/// prevents "magic strings" from drifting between services.
/// </summary>
public static class MessagingTopology
{
    /// <summary>Topic exchange every inventory event is published to.</summary>
    public const string InventoryExchange = "inventory.events";

    /// <summary>Exchange an event is moved to when a consumer cannot handle it.</summary>
    public const string DeadLetterExchange = "inventory.events.dlx";

    /// <summary>Queue consumed by the Alert service (low-stock detection).</summary>
    public const string AlertQueue = "alert.inventory";

    /// <summary>Queue consumed by the Reporting service (read model projection).</summary>
    public const string ReportingQueue = "reporting.inventory";

    /// <summary>Routing key pattern covering every stock event.</summary>
    public const string AllStockEventsRoutingKey = "stock.*";

    /// <summary>
    /// How many unacknowledged messages a consumer may hold. One at a time keeps
    /// the Alert projection in a deterministic order per consumer.
    /// </summary>
    public const ushort ConsumerPrefetch = 16;

    /// <summary>
    /// Dead-letter queue of <paramref name="queue"/>: where a message ends up when
    /// a consumer has already retried it once, so a poison message can never
    /// block or be silently dropped (it stays visible in the management UI).
    /// </summary>
    public static string DeadLetterQueue(string queue) => $"{queue}.dead";
}
