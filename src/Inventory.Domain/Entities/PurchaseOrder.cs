namespace Inventory.Domain.Entities;

public enum PurchaseOrderStatus
{
    Pending = 0,
    Received = 1,
    Cancelled = 2,
    Draft = 3
}

public enum PurchasePaymentStatus
{
    Unpaid = 0,
    Paid = 1
}

public class PurchaseOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public string SupplierNameSnapshot { get; set; } = "";

    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Pending;
    public PurchasePaymentStatus PaymentStatus { get; set; } = PurchasePaymentStatus.Unpaid;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedByUserId { get; set; } = "";
    public string CreatedByUserDisplayName { get; set; } = "";
    public string? Note { get; set; }
    
    // Tax System Fields
    public bool IsTaxInclusive { get; set; } = true;
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
    
    // Tax System Fields
    public bool IsTaxInclusive { get; set; }
    public decimal LineSubtotal { get; set; }
    public decimal LineVatAmount { get; set; }
    public decimal LineManufacturingTaxAmount { get; set; }
    public decimal LineTotal { get; set; }
}
