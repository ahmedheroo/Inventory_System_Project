using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace InventorySystem.BuildingBlocks.Messaging;

/// <summary>
/// RabbitMQ connection settings, read from the <c>RabbitMq</c> configuration
/// section. The defaults match <c>docker-compose.yml</c> so a local run works
/// without any configuration at all.
/// </summary>
public sealed record RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public bool Enabled { get; init; } = true;

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = "inventory";

    public string Password { get; init; } = "inventory";

    public string VirtualHost { get; init; } = "/";

    /// <summary>Name the broker shows in its connections list.</summary>
    public string? ClientName { get; init; }

    /// <summary>Seconds to wait for a TCP connection before failing an attempt.</summary>
    public int ConnectionTimeoutSeconds { get; init; } = 10;

    public static RabbitMqOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        return new RabbitMqOptions
        {
            Enabled = ReadBool(section, "Enabled", true),
            HostName = section["HostName"] is { Length: > 0 } hostName ? hostName : "localhost",
            Port = ReadInt(section, "Port", 5672),
            UserName = section["UserName"] is { Length: > 0 } userName ? userName : "inventory",
            Password = section["Password"] is { Length: > 0 } password ? password : "inventory",
            VirtualHost = section["VirtualHost"] is { Length: > 0 } virtualHost ? virtualHost : "/",
            ClientName = section["ClientName"],
            ConnectionTimeoutSeconds = ReadInt(section, "ConnectionTimeoutSeconds", 10)
        };
    }

    private static bool ReadBool(IConfiguration section, string key, bool fallback) =>
        bool.TryParse(section[key], out var value) ? value : fallback;

    private static int ReadInt(IConfiguration section, string key, int fallback) =>
        int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
