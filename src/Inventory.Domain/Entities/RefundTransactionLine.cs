namespace Inventory.Domain.Entities;

public class RefundTransactionLine
{
    public long Id { get; set; }
    
    public long RefundTransactionId { get; set; }
    public RefundTransaction? RefundTransaction { get; set; }

    // Links to original order lines
    public long? SalesOrderLineId { get; set; }
    public SalesOrderLine? SalesOrderLine { get; set; }
    
    public long? PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine? PurchaseOrderLine { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    
    public decimal Quantity { get; set; }
    public string? BatchNumber { get; set; }
    public long? ProductBatchId { get; set; }
    public ProductBatch? ProductBatch { get; set; }
    
    // Financial impact of this line refund (optional/calculated)
    public decimal UnitPriceSnapshot { get; set; }
    public decimal LineRefundAmount { get; set; }
}
