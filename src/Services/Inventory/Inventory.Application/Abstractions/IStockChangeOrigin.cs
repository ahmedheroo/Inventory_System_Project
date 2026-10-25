namespace InventorySystem.Stock.Application.Abstractions;

/// <summary>
/// Who asked for the operation currently being handled, as a short label the caller supplied.
///
/// It exists for one reason: several people work the same system at once, and a screen that says
/// "something moved" is less useful than one that says "your receipt was confirmed, and this other
/// terminal moved something too". The value is a *label*, never a credential — nothing is
/// authorised by it, and an operation with no origin is a perfectly ordinary operation (a script, a
/// migration, a service-to-service call).
/// </summary>
public interface IStockChangeOrigin
{
    /// <summary>The calling terminal's label, or null when it did not name one.</summary>
    string? Value { get; }
}

/// <summary>The origin of an operation nobody named: a script, a test, another service.</summary>
public sealed class UnspecifiedStockChangeOrigin : IStockChangeOrigin
{
    public string? Value => null;
}
