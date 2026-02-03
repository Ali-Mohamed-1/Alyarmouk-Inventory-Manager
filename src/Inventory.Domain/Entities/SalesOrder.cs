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
    /// </summary>
    public PaymentStatus PaymentStatus { get; set; } = PaymentStatus.Pending;

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