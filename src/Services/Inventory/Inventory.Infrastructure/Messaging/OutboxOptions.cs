using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace InventorySystem.Stock.Infrastructure.Messaging;

/// <summary>
/// Settings of the outbox dispatcher (configuration section <c>Outbox</c>).
/// The defaults are deliberately modest: a few events per second are plenty for
/// this system, and a short poll keeps the alert latency low.
/// </summary>
public sealed record OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the dispatcher looks for pending events.</summary>
    public int PollSeconds { get; init; } = 5;

    /// <summary>How many pending events are handled per pass.</summary>
    public int BatchSize { get; init; } = 50;

    /// <summary>Backoff after the first failed attempt.</summary>
    public int InitialRetrySeconds { get; init; } = 2;

    /// <summary>Upper bound of the exponential backoff.</summary>
    public int MaxRetrySeconds { get; init; } = 60;

    /// <summary>
    /// Exponential backoff for the next attempt: 2s, 4s, 8s, … capped. There is
    /// no attempt limit on purpose — an event that cannot be delivered stays in
    /// the outbox until the broker accepts it, which is the whole point of the
    /// outbox.
    /// </summary>
    public TimeSpan BackoffFor(int failedAttempts)
    {
        if (failedAttempts < 1)
        {
            failedAttempts = 1;
        }

        // Guard the shift: 2^30 seconds would overflow long before it is reached.
        var exponent = Math.Min(failedAttempts - 1, 20);
        var seconds = (double)InitialRetrySeconds * Math.Pow(2, exponent);

        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetrySeconds));
    }

    public static OutboxOptions FromConfiguration(IConfiguration configuration)
    {
        var section = configuration.GetSection(SectionName);

        return new OutboxOptions
        {
            PollSeconds = ReadInt(section, "PollSeconds", 5),
            BatchSize = ReadInt(section, "BatchSize", 50),
            InitialRetrySeconds = ReadInt(section, "InitialRetrySeconds", 2),
            MaxRetrySeconds = ReadInt(section, "MaxRetrySeconds", 60)
        };
    }

    private static int ReadInt(IConfiguration section, string key, int fallback) =>
        int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;
}
