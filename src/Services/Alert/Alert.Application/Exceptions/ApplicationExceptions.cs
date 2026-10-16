namespace InventorySystem.Alerts.Application.Exceptions;

/// <summary>Request data that cannot be satisfied by the current state (mapped to HTTP 400).</summary>
public class ValidationException : Exception
{
    public ValidationException(string message) : base(message)
    {
    }
}

/// <summary>An entity the caller asked for does not exist (mapped to HTTP 404).</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}
