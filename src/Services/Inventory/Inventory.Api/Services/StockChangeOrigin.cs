using InventorySystem.Stock.Application.Abstractions;

namespace InventorySystem.Stock.Api.Services;

/// <summary>
/// The terminal that asked for the operation, as it identified itself in the request header
/// <c>X-Origin-Id</c> (the console sends the station its browser belongs to).
///
/// The value is sanitised and truncated before it is echoed to watchers, and it is never used to
/// authorise anything: it is a label on a live notification, so anything that is not a plausible
/// short identifier is dropped rather than passed on.
/// </summary>
public sealed class StockChangeOrigin : IStockChangeOrigin
{
    public const string HeaderName = "X-Origin-Id";

    private const int MaxLength = 40;

    private readonly IHttpContextAccessor _accessor;

    public StockChangeOrigin(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string? Value
    {
        get
        {
            var raw = _accessor.HttpContext?.Request.Headers[HeaderName].ToString();

            return Sanitise(raw);
        }
    }

    /// <summary>
    /// Keeps what can be part of an identifier (letters, digits, `-`, `_`, `.`) up to a fixed length.
    /// An empty or unusable header means "did not identify itself", which is a normal caller.
    /// </summary>
    public static string? Sanitise(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var cleaned = new string(
            raw.Trim()
                .Where(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.')
                .Take(MaxLength)
                .ToArray());

        return cleaned.Length == 0 ? null : cleaned;
    }
}
