namespace InventorySystem.Reports.Application.Exceptions;

/// <summary>
/// The request is well-formed but not valid in context (for example a movement
/// report whose <c>from</c> is after its <c>to</c>). Mapped to HTTP 400.
/// </summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}

/// <summary>
/// The Product service (which owns products, warehouses and prices) could not be
/// reached, so a report could not be rendered. Mapped to HTTP 503 — the request
/// is retryable, not invalid.
/// </summary>
public class CatalogUnavailableException : Exception
{
    public CatalogUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
