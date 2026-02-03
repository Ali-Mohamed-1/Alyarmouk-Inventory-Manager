namespace Inventory.Domain.Entities;

/// <summary>
/// Audit trail for refund transactions.
/// Records all refunds processed in the system for both Sales and Purchase Orders.
/// </summary>
public class RefundTransaction
{
    public long Id { get; set; }
    
    /// <summary>
    /// Type of refund: Sales Order or Purchase Order
    /// </summary>
    public RefundType Type { get; set; }
    
    /// <summary>
    /// Reference to Sales Order if this is a sales refund
    /// </summary>
    public long? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    
    /// <summary>
    /// Reference to Purchase Order if this is a purchase refund
    /// </summary>
    public long? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    
    /// <summary>
    /// Monetary amount refunded
    /// </summary>
    public decimal Amount { get; set; }
    
    /// <summary>
    /// Reason for the refund (e.g., damaged goods, customer return, etc.)
    /// </summary>
    public string? Reason { get; set; }
    
    /// <summary>
    /// When the refund was processed
    /// </summary>
    public DateTimeOffset ProcessedUtc { get; set; } = DateTimeOffset.UtcNow;
    
    /// <summary>
    /// User who processed the refund
    /// </summary>
    public string ProcessedByUserId { get; set; } = "";
    public string ProcessedByUserDisplayName { get; set; } = "";
    
    /// <summary>
    /// Additional notes about the refund
    /// </summary>
    public string? Note { get; set; }

    public List<RefundTransactionLine> Lines { get; set; } = new();
}

/// <summary>
/// Type of refund transaction
/// </summary>
public enum RefundType
{
    SalesOrder = 1,
    PurchaseOrder = 2
}
