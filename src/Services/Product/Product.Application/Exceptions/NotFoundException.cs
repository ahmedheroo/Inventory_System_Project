namespace InventorySystem.Products.Application.Exceptions;


/// Thrown when a requested resource does not exist. The API layer maps this to HTTP 404.

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }

    public static NotFoundException For(string resource, object id) =>
        new($"{resource} with id '{id}' was not found.");
}
