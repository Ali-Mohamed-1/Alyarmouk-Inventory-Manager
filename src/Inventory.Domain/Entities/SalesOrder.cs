namespace Inventory.Domain.Entities;

public enum SalesOrderStatus
{
    Pending = 0,
    Done = 1,
    Cancelled = 2
}

public enum PaymentMethod
{
    Cash = 1,
    Check = 2,
    BankTransfer = 3
}

public enum PaymentStatus
{
    Pending = 0,
    Paid = 1,
    PartiallyPaid = 2
}

public  class SalesOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string CustomerNameSnapshot { get; set; } = "";

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Pending;

    /// <summary>
    /// Order creation date (local business date).
    /// </summary>
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the customer is expected to pay.
    /// </summary>
    public DateTimeOffset DueDate { get; set; }

    /// <summary>
    /// Payment method used for this order (cash / check).
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    /// <summary>
    /// Overall payment status for this order. 
    /// Managed by RecalculatePaymentStatus(), do not set manually.
    /// </summary>
    public PaymentStatus PaymentStatus { get; private set; } = PaymentStatus.Pending;

    public List<PaymentRecord> Payments { get; set; } = new();

    public void RecalculatePaymentStatus()
    {
        // GUARD: Ensure Payments collection is loaded before recalculation
        if (Payments == null)
            throw new InvalidOperationException(
                $"Cannot recalculate payment status for SalesOrder {Id}: Payments collection is not loaded. " +
                "Ensure .Include(o => o.Payments) is used when loading the order.");

        // Collection Logic: Only count MONEY IN
        var totalPaid = Payments
            .Where(p => p.PaymentType == PaymentRecordType.Payment)
            .Sum(p => p.Amount);
            
        // Refund Logic: Only count MONEY OUT
        var totalRefunded = Payments
            .Where(p => p.PaymentType == PaymentRecordType.Refund)
            .Sum(p => p.Amount);

        var netCash = totalPaid - totalRefunded;

        // PaymentStatus describes COLLECTION progress only.
        // Refunds DO NOT degrade PaymentStatus.
        
        if (totalPaid == 0)
        {
            PaymentStatus = PaymentStatus.Pending;
        }
        else if (totalPaid < TotalAmount)
        {
            PaymentStatus = PaymentStatus.PartiallyPaid;
        }
        else // totalPaid >= TotalAmount
        {
            PaymentStatus = PaymentStatus.Paid;
        }

        // Integrity Checks
        if (netCash < 0)
             throw new InvalidOperationException($"Invalid state: Order {Id} has negative NetCash ({netCash:C}). Refunded more than paid.");
    }
    
    public decimal GetTotalPaid()
    {
        if (Payments == null) return 0;
        return Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount);
    }

    public decimal GetTotalRefunded()
    {
        if (Payments == null) return 0;
        return Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount);
    }

    public decimal GetNetCash()
    {
        return GetTotalPaid() - GetTotalRefunded();
    }

    /// <summary>
    /// Money we still need to collect to reach the Order Total.
    /// Refunds do NOT increase this.
    /// </summary>
    public decimal GetPendingAmount()
    {
        var paid = GetTotalPaid();
        return Math.Max(0, TotalAmount - paid);
    }

    /// <summary>
    /// Money we hold in excess of the Order Total (or if order total was reduced).
    /// </summary>
    public decimal GetRefundDue()
    {
        var net = GetNetCash();
        return Math.Max(0, net - TotalAmount);
    }

    public bool IsOverdue() => DueDate < DateTimeOffset.UtcNow && PaymentStatus != PaymentStatus.Paid;

    public decimal GetDeservedAmount()
    {
        return IsOverdue() ? GetPendingAmount() : 0;
    }

    public decimal GetTotalPending()
    {
        return GetPendingAmount();
    }

    /// <summary>
    /// For check payments: whether we received the check.
    /// </summary>
    public bool? CheckReceived { get; set; }

    /// <summary>
    /// For check payments: date when the check was received.
    /// </summary>
    public DateTimeOffset? CheckReceivedDate { get; set; }

    /// <summary>
    /// For check payments: whether the check has been cashed.
    /// </summary>
    public bool? CheckCashed { get; set; }

    /// <summary>
    /// For check payments: date when the check was cashed.
    /// </summary>
    public DateTimeOffset? CheckCashedDate { get; set; }
    
    /// <summary>
    /// For bank transfer payments: unique transfer identifier/reference.
    /// </summary>
    public string? TransferId { get; set; }

    /// <summary>
    /// Optional path or identifier to an Invoice PDF attachment stored for this order.
    /// The web layer is responsible for saving the actual file and providing the path.
    /// </summary>
    public string? InvoicePath { get; set; }

    /// <summary>
    /// When the Invoice PDF attachment was last uploaded/updated.
    /// </summary>
    public DateTimeOffset? InvoiceUploadedUtc { get; set; }

    /// <summary>
    /// Optional path or identifier to a Receipt PDF attachment stored for this order.
    /// </summary>
    public string? ReceiptPath { get; set; }

    /// <summary>
    /// When the Receipt PDF attachment was last uploaded/updated.
    /// </summary>
    public DateTimeOffset? ReceiptUploadedUtc { get; set; }

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedByUserId { get; set; } = "";
    public string CreatedByUserDisplayName { get; set; } = "";
    public string? Note { get; set; }
    
    // Tax System Fields
    public bool IsTaxInclusive { get; set; } = false;
    public bool ApplyVat { get; set; } = true;
    public bool ApplyManufacturingTax { get; set; } = true;
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal ManufacturingTaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    
    /// <summary>
    /// Total amount that has been refunded for this order.
    /// Must never exceed TotalAmount.
    /// </summary>
    public decimal RefundedAmount { get; set; }
    
    public List<SalesOrderLine> Lines { get; set; } = new();

    /// <summary>
    /// Indicates if this order was imported as a historical record.
    /// Historical orders do not affect stock creation-time, only when explicitly activated.
    /// </summary>
    public bool IsHistorical { get; set; }

    /// <summary>
    /// For historical orders, tracks whether the stock impact has been applied.
    /// </summary>
    public bool IsStockProcessed { get; set; }
}

public class SalesOrderLine
{
    public long Id { get; set; }
    public long SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductNameSnapshot { get; set; } = "";

    /// <summary>
    /// Optional batch/lot number for this line, when stock is tracked per batch.
    /// </summary>
    public string? BatchNumber { get; set; }
    
    public long? ProductBatchId { get; set; }
    public ProductBatch? ProductBatch { get; set; }

    public decimal Quantity { get; set; }
    public string UnitSnapshot { get; set; } = "";
    public decimal UnitPrice { get; set; } // Price per unit at time of order (snapshot)
    
    /// <summary>
    /// Total quantity of this line that has been refunded.
    /// Must never exceed Quantity.
    /// </summary>
    public decimal RefundedQuantity { get; set; }
    
    // Tax System Fields
    public decimal LineSubtotal { get; set; }
    public decimal LineVatAmount { get; set; }
    public decimal LineManufacturingTaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}