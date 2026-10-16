namespace InventorySystem.Products.Domain.Exceptions;


/// Thrown when a business rule / entity invariant is violated.
/// The API layer maps this to HTTP 400.

public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}
