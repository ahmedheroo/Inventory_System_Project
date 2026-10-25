using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Services;

/// <summary>The console's own token, valid now. Renews it when it has to.</summary>
public interface IServiceTokenProvider
{
    Task<string> AccessTokenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Holds the token the console presents to the Inventory service's live view — one token for the whole
/// console, not one per page or per person, because the relay is one client and the pages share it.
///
/// It is fetched when it is first needed and then kept until it is nearly expired: a token is a
/// signed statement with a lifetime, so asking for a new one on every connection attempt would be
/// noise, and keeping one until it is refused would be a live view that stops without saying why.
/// Renewal is guarded by a lock, so a burst of connections after a restart costs one call.
/// </summary>
public sealed class ServiceTokenProvider : IServiceTokenProvider, IDisposable
{
    /// <summary>Enough notice that a connection opened just now is not opened with a dying token.</summary>
    public static readonly TimeSpan DefaultRefreshMargin = TimeSpan.FromMinutes(2);

    private readonly IIdentityApi _identity;
    private readonly ServiceCredentialOptions _credentials;
    private readonly TimeProvider _clock;
    private readonly ILogger<ServiceTokenProvider> _logger;
    private readonly TimeSpan _refreshMargin;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string? _token;
    private DateTimeOffset _expiresAt;

    public ServiceTokenProvider(
        IIdentityApi identity,
        ServiceCredentialOptions credentials,
        TimeProvider clock,
        ILogger<ServiceTokenProvider> logger,
        TimeSpan? refreshMargin = null)
    {
        _identity = identity;
        _credentials = credentials;
        _clock = clock;
        _logger = logger;
        _refreshMargin = refreshMargin ?? DefaultRefreshMargin;
    }

    /// <summary>When the token in hand stops working, or null when there is none.</summary>
    public DateTimeOffset? ExpiresAt => _token is null ? null : _expiresAt;

    public async Task<string> AccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (Usable(_clock.GetUtcNow()) is { } cached)
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);

        try
        {
            // The token may have been renewed while this caller was waiting for the lock: the whole
            // point of the lock is that the waiters share one answer.
            if (Usable(_clock.GetUtcNow()) is { } renewed)
            {
                return renewed;
            }

            var issued = await _identity.ServiceTokenAsync(
                new ClientCredentialsRequest(_credentials.ClientId, _credentials.ClientSecret),
                cancellationToken);

            _token = issued.AccessToken;
            _expiresAt = issued.ExpiresAt;

            _logger.LogInformation(
                "The console renewed its own token; the live view follows until {ExpiresAt:u}.",
                _expiresAt);

            return _token;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();

    private string? Usable(DateTimeOffset now) =>
        _token is not null && now + _refreshMargin < _expiresAt ? _token : null;
}
