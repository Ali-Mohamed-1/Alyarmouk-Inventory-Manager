namespace Inventory.Domain.Entities;

/// <summary>
/// Represents a sale made TO a supplier (e.g. selling surplus or returned goods back to a supplier).
/// Distinct from SalesOrder which is a sale TO a customer.
/// </summary>
public class SupplierSalesOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";

    // Linked to Supplier, NOT to Customer
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string SupplierNameSnapshot { get; set; } = "";

    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Pending;

    /// <summary>
    /// Order creation date (local business date).
    /// </summary>
    public DateTimeOffset OrderDate { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the supplier is expected to pay.
    /// </summary>
    public DateTimeOffset DueDate { get; set; }

    /// <summary>
    /// Overall status of the bookkeeping netting.
    /// Pending = No changes to balance.
    /// Paid = Subtract the debt.
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

    // ─── Invariant guards ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns true if the order can be cancelled.
    /// </summary>
    public bool CanCancel(out string error)
    {
        if (Status == SalesOrderStatus.Cancelled)
        {
            error = "Order is already cancelled.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    // ─── Document attachments ───────────────────────────────────────────────────

    public string? InvoicePath { get; set; }
    public DateTimeOffset? InvoiceUploadedUtc { get; set; }
    public string? ReceiptPath { get; set; }
    public DateTimeOffset? ReceiptUploadedUtc { get; set; }

    // ─── Audit ──────────────────────────────────────────────────────────────────

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedByUserId { get; set; } = "";
    public string CreatedByUserDisplayName { get; set; } = "";
    public string? Note { get; set; }

    // ─── Tax fields ─────────────────────────────────────────────────────────────

    public bool IsTaxInclusive { get; set; } = false;
    public bool ApplyVat { get; set; } = true;
    public bool ApplyManufacturingTax { get; set; } = true;
    public decimal Subtotal { get; set; }
    public decimal VatAmount { get; set; }
    public decimal ManufacturingTaxAmount { get; set; }
    public decimal TotalAmount { get; set; }
    
    /// <summary>
    /// Effective total after quantity refunds. Used for calculating debt.
    /// </summary>
    public decimal EffectiveTotal { get; set; }
    
    public decimal EffectiveSubtotal { get; set; }
    public decimal EffectiveVatAmount { get; set; }
    public decimal EffectiveManufacturingTaxAmount { get; set; }
    
    /// <summary>
    /// Total amount that has been refunded for this order.
    /// Must never exceed TotalAmount.
    /// </summary>
    public decimal RefundedAmount { get; set; }

    public List<SupplierSalesOrderLine> Lines { get; set; } = new();
}

public class SupplierSalesOrderLine
{
    public long Id { get; set; }
    public long SupplierSalesOrderId { get; set; }
    public SupplierSalesOrder? SupplierSalesOrder { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductNameSnapshot { get; set; } = "";

    /// <summary>
    /// Optional batch/lot number for this line.
    /// </summary>
    public string? BatchNumber { get; set; }

    public long? ProductBatchId { get; set; }
    public ProductBatch? ProductBatch { get; set; }

    public decimal Quantity { get; set; }
    public string UnitSnapshot { get; set; } = "";

    /// <summary>
    /// Price per unit at time of order (snapshot).
    /// </summary>
    public decimal UnitPrice { get; set; }

    /// <summary>
    /// Total quantity of this line that has been refunded (stock returned).
    /// Must never exceed Quantity.
    /// </summary>
    public decimal RefundedQuantity { get; set; }

    // Tax fields
    public decimal LineSubtotal { get; set; }
    public decimal LineVatAmount { get; set; }
    public decimal LineManufacturingTaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
