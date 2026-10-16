namespace InventorySystem.Products.Application.Exceptions;


/// Thrown when a request is well-formed but references data that is invalid in
/// context (for example an unknown category id). Mapped to HTTP 400.

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
