namespace InventorySystem.Alerts.Domain.Exceptions;

/// <summary>
/// Thrown when an alert rule is violated (negative quantity, missing message,
/// refreshing an already resolved alert). The API layer maps this to HTTP 400.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
