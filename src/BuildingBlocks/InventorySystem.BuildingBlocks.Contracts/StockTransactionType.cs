namespace InventorySystem.BuildingBlocks.Contracts;

/// <summary>
/// Kinds of stock transaction recorded by the Inventory service (see the
/// "Transaction types" section of the specification).
/// </summary>
public enum StockTransactionType
{
    RECEIVE = 0,
    ISSUE = 1,
    TRANSFER_IN = 2,
    TRANSFER_OUT = 3,
    ADJUSTMENT = 4
}
