namespace Inventory.Domain.Entities;

public enum PurchaseOrderStatus
{
    Pending = 0,
    Received = 1,
    Cancelled = 2
}

public enum PurchasePaymentStatus
{
    Unpaid = 0,
    Paid = 1,
    PartiallyPaid = 2
}

public class PurchaseOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string SupplierNameSnapshot { get; set; } = "";

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Pending;

    /// <summary>
    /// Payment method used for this order.
    /// </summary>
    public PaymentMethod PaymentMethod { get; set; } = PaymentMethod.Cash;

    public PurchasePaymentStatus PaymentStatus { get; private set; } = PurchasePaymentStatus.Unpaid;

    public List<PaymentRecord> Payments { get; set; } = new();

    public void RecalculatePaymentStatus()
    {
        // GUARD: Ensure Payments collection is loaded before recalculation
        if (Payments == null)
            throw new InvalidOperationException(
                $"Cannot recalculate payment status for PurchaseOrder {Id}: Payments collection is not loaded. " +
                "Ensure .Include(o => o.Payments) is used when loading the order.");

        var totalPaid = Payments
            .Where(p => p.PaymentType == PaymentRecordType.Payment)
            .Sum(p => p.Amount);
            
        var totalRefunded = Payments
            .Where(p => p.PaymentType == PaymentRecordType.Refund)
            .Sum(p => p.Amount);

        var netCash = totalPaid - totalRefunded;

        // PaymentStatus is about Collection/Payment Completion.
        // Refunds do NOT degrade this status.
        
        if (totalPaid == 0)
        {
            PaymentStatus = PurchasePaymentStatus.Unpaid;
        }
        else if (totalPaid < TotalAmount)
        {
            PaymentStatus = PurchasePaymentStatus.PartiallyPaid;
        }
        else
        {
            PaymentStatus = PurchasePaymentStatus.Paid;
        }
        
        // Validation of physical reality
        if (netCash < 0)
             throw new InvalidOperationException($"Invalid state: PurchaseOrder {Id} has negative NetCash ({netCash:C}). Refunded more than paid.");
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
    /// Money we still need to pay to the supplier.
    /// Refunds do NOT increase this.
    /// </summary>
    public decimal GetPendingAmount()
    {
        var paid = GetTotalPaid();
        return Math.Max(0, TotalAmount - paid);
    }

    /// <summary>
    /// Money the supplier owes us (e.g. after a return/refund).
    /// </summary>
    public decimal GetRefundDue()
    {
        var net = GetNetCash();
        return Math.Max(0, net - TotalAmount);
    }

    public bool IsOverdue() => PaymentDeadline.HasValue && PaymentDeadline.Value < DateTimeOffset.UtcNow && PaymentStatus != PurchasePaymentStatus.Paid;

    public decimal GetDeservedAmount()
    {
        return IsOverdue() ? GetPendingAmount() : 0;
    }

    public decimal GetTotalPending()
    {
        return GetPendingAmount();
    }

    /// <summary>
    /// When payment to the supplier is expected/due.
    /// </summary>
    public DateTimeOffset? PaymentDeadline { get; set; }

    /// <summary>
    /// For check payments: whether we received/issued the check.
    /// </summary>
    public bool? CheckReceived { get; set; }

    /// <summary>
    /// For check payments: date when the check was received/issued.
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
    /// Order creation date (local business date).
    /// </summary>
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;

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
    public decimal ReceiptExpenses { get; set; } // Shipping, handling, etc.
    public decimal TotalAmount { get; set; }
    public decimal RefundedAmount { get; set; }
    
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
    
    public List<PurchaseOrderLine> Lines { get; set; } = new();

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

public class PurchaseOrderLine
{
    public long Id { get; set; }
    public long PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductNameSnapshot { get; set; } = "";

    /// <summary>
    /// Optional batch/lot number for the quantity received on this line.
    /// </summary>
    public string? BatchNumber { get; set; }

    public decimal Quantity { get; set; }
    public string UnitSnapshot { get; set; } = "";
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Total quantity of this line that has been refunded (returned to supplier).
    /// Must never exceed Quantity.
    /// </summary>
    public decimal RefundedQuantity { get; set; }
    
    // Tax System Fields
    public bool IsTaxInclusive { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal LineVatAmount { get; set; }
    public decimal LineManufacturingTaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
