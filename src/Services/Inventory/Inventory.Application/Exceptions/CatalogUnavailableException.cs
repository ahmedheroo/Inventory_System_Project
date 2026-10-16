namespace InventorySystem.Stock.Application.Exceptions;


/// The Product service (which owns products and warehouses) could not be
/// reached, so the precondition of a stock operation could not be verified.
/// Mapped to HTTP 503 — the request is retryable, not invalid.

public class CatalogUnavailableException : Exception
{
    public CatalogUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
