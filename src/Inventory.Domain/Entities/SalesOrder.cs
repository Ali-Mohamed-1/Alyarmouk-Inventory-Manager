namespace Inventory.Domain.Entities;

public  class SalesOrder
{
    public long Id { get; set; }
    public string OrderNumber { get; set; } = "";
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string CustomerNameSnapshot { get; set; } = "";
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string CreatedByUserId { get; set; } = "";
    public string CreatedByUserDisplayName { get; set; } = "";
    public string? Note { get; set; }
    public List<SalesOrderLine> Lines { get; set; } = new();

    public bool IsTaxInclusive { get; set; } = true; // Sales always include tax
    public decimal Subtotal { get; set; } // Base amount before taxes
    public decimal VatAmount { get; set; } // VAT amount (14%)
    public decimal ManufacturingTaxAmount { get; set; } // Manufacturing tax amount (1%)
    public decimal TotalAmount { get; set; } // Final total including all taxes
}

public class SalesOrderLine
{
    public long Id { get; set; }
    public long SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    public int ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductNameSnapshot { get; set; } = "";
    public decimal Quantity { get; set; }
    public string UnitSnapshot { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public bool IsTaxInclusive { get; set; } = true; // Inherited from order
    public decimal LineSubtotal { get; set; } // Base amount for this line
    public decimal LineVatAmount { get; set; } // VAT for this line
    public decimal LineManufacturingTaxAmount { get; set; } // Manufacturing tax for this line
    public decimal LineTotal { get; set; } // Total for this line including taxes
}