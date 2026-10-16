namespace InventorySystem.Stock.Application.Exceptions;


/// The request is well-formed but not valid in context (unknown or deactivated
/// product/warehouse, no stock to transfer). Mapped to HTTP 400.

public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}
