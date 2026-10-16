using InventorySystem.Alerts.Domain.Exceptions;

namespace InventorySystem.Alerts.Domain.Entities;

/// <summary>What kind of inventory problem an alert reports.</summary>
public enum AlertType
{
    /// <summary>Quantity is at or below the minimum, but there is still stock.</summary>
    LOW_STOCK = 0,

    /// <summary>Quantity has reached zero.</summary>
    OUT_OF_STOCK = 1
}

/// <summary>Lifecycle of an alert.</summary>
public enum AlertStatus
{
    /// <summary>The condition still holds (or has not been contradicted by a later event).</summary>
    ACTIVE = 0,

    /// <summary>A later event showed the quantity is back above the minimum.</summary>
    RESOLVED = 1
}

/// <summary>
/// A low-stock alert (spec section 7.3). Alerts are raised by the consumer from
/// inventory events, never entered by hand, so this entity's job is to hold the
/// condition that was observed and the state a user can act on.
///
/// Duplicate policy (spec: "duplicate alert behavior is defined"): as long as an
/// alert for a (product, warehouse) pair is <see cref="AlertStatus.ACTIVE"/>, a
/// newer event updates that row instead of adding another one, so a fluctuating
/// quantity produces one alert, not hundreds. When a later event shows the stock
/// is back above the minimum, the alert is resolved; if the stock runs low again
/// later, a new alert is raised and the resolved history is kept.
/// </summary>
public class Alert
{
    public const int MessageMaxLength = 300;

    // Required by EF Core.
    private Alert()
    {
        Message = string.Empty;
    }

    private Alert(
        int productId,
        int warehouseId,
        AlertType type,
        string message,
        int currentQuantity,
        int minimumStockLevel,
        Guid sourceEventId,
        DateTime createdAt)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Type = type;
        Message = message;
        CurrentQuantity = currentQuantity;
        MinimumStockLevel = minimumStockLevel;
        SourceEventId = sourceEventId;
        CreatedAt = createdAt;
        Status = AlertStatus.ACTIVE;
    }

    public int Id { get; private set; }

    public int ProductId { get; private set; }

    public int WarehouseId { get; private set; }

    public AlertType Type { get; private set; }

    public string Message { get; private set; }

    /// <summary>Quantity on hand when the alert was raised or last refreshed.</summary>
    public int CurrentQuantity { get; private set; }

    public int MinimumStockLevel { get; private set; }

    public bool IsRead { get; private set; }

    public DateTime? ReadAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Last time a newer event updated this alert.</summary>
    public DateTime UpdatedAt { get; private set; }

    public DateTime? ResolvedAt { get; private set; }

    public AlertStatus Status { get; private set; }

    /// <summary>The event that last touched this alert (audit / traceability).</summary>
    public Guid SourceEventId { get; private set; }

    public bool IsActive => Status == AlertStatus.ACTIVE;

    public static Alert Raise(
        int productId,
        int warehouseId,
        AlertType type,
        string message,
        int currentQuantity,
        int minimumStockLevel,
        Guid sourceEventId,
        DateTime? createdAt = null)
    {
        if (productId <= 0)
        {
            throw new DomainException("Product id must be positive.");
        }

        if (warehouseId <= 0)
        {
            throw new DomainException("Warehouse id must be positive.");
        }

        if (currentQuantity < 0)
        {
            throw new DomainException("Quantity cannot be negative.");
        }

        if (minimumStockLevel < 0)
        {
            throw new DomainException("Minimum stock level cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainException("An alert must carry a message.");
        }

        if (message.Length > MessageMaxLength)
        {
            throw new DomainException($"An alert message must be at most {MessageMaxLength} characters.");
        }

        if (sourceEventId == Guid.Empty)
        {
            throw new DomainException("An alert must reference the event that raised it.");
        }

        var now = createdAt ?? DateTime.UtcNow;

        // The caller is responsible for only raising this when the condition
        // holds; the guard keeps a bad projection from creating a meaningless alert.
        if (currentQuantity > minimumStockLevel)
        {
            throw new DomainException(
                $"A low-stock alert requires the quantity ({currentQuantity}) to be at or below the minimum ({minimumStockLevel}).");
        }

        return new Alert(
            productId,
            warehouseId,
            type,
            message.Trim(),
            currentQuantity,
            minimumStockLevel,
            sourceEventId,
            now)
        {
            UpdatedAt = now
        };
    }

    /// <summary>
    /// Applies a newer observation to this (still active) alert: the quantity,
    /// the type (low stock → out of stock) and the message are brought up to
    /// date. Read state is preserved on purpose — a user who already saw the
    /// alert should not have it marked unread again.
    /// </summary>
    public void Refresh(
        AlertType type,
        string message,
        int currentQuantity,
        int minimumStockLevel,
        Guid sourceEventId,
        DateTime? updatedAt = null)
    {
        if (!IsActive)
        {
            throw new DomainException("A resolved alert cannot be refreshed; raise a new one instead.");
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            throw new DomainException("An alert must carry a message.");
        }

        if (message.Length > MessageMaxLength)
        {
            throw new DomainException($"An alert message must be at most {MessageMaxLength} characters.");
        }

        if (currentQuantity < 0 || minimumStockLevel < 0)
        {
            throw new DomainException("Quantity and minimum stock level cannot be negative.");
        }

        Type = type;
        Message = message.Trim();
        CurrentQuantity = currentQuantity;
        MinimumStockLevel = minimumStockLevel;
        SourceEventId = sourceEventId;
        UpdatedAt = updatedAt ?? DateTime.UtcNow;
    }

    /// <summary>Resolves the alert because the quantity is back above the minimum.</summary>
    public void Resolve(DateTime resolvedAt, Guid sourceEventId)
    {
        if (!IsActive)
        {
            return;
        }

        Status = AlertStatus.RESOLVED;
        ResolvedAt = resolvedAt;
        UpdatedAt = resolvedAt;
        SourceEventId = sourceEventId == Guid.Empty ? SourceEventId : sourceEventId;
    }

    /// <summary>Marks the alert as read. Idempotent: marking it twice keeps the first timestamp.</summary>
    public bool MarkAsRead(DateTime? readAt = null)
    {
        if (IsRead)
        {
            return false;
        }

        IsRead = true;
        ReadAt = readAt ?? DateTime.UtcNow;

        return true;
    }
}
