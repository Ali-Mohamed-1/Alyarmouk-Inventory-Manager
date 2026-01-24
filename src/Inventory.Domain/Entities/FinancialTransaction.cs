namespace Inventory.Domain.Entities;

public enum FinancialTransactionType
{
    Expense = 1,  // Money going out (costs)
    Revenue = 2   // Money coming in (sales)
}

public sealed class FinancialTransaction
{
    public long Id { get; set; }
    
    public FinancialTransactionType Type { get; set; }
    public decimal Amount { get; set; } // Always positive, Type indicates direction
    
    // Reference to related transaction
    public long? InventoryTransactionId { get; set; }
    public InventoryTransaction? InventoryTransaction { get; set; }
    
    public long? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    
    public DateTimeOffset TimestampUtc { get; set; }
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string? Note { get; set; }
}
