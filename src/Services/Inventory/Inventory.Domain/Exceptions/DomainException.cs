namespace InventorySystem.Stock.Domain.Exceptions;

/// <summary>
/// Thrown when a stock business rule is violated (over-issue, negative quantity,
/// invalid transfer). The API layer maps this to HTTP 400.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
