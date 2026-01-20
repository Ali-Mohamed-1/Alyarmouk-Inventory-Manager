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
}