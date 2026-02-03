namespace Inventory.Domain.Entities;

public enum FinancialTransactionType
{
    Expense = 1,  // Money going out (costs)
    Revenue = 2   // Money coming in (sales)
}

/// <summary>
/// Categorizes expenses that are purely internal/overhead and not directly tied
/// to the cost of goods (e.g. rent, salaries).
/// </summary>
public enum InternalExpenseType
{
    Rental = 1,
    Salaries = 2,
    Labor = 3,
    Commission = 4,
    Zakat = 5,
    Other = 6
}

public sealed class FinancialTransaction
{
    public long Id { get; set; }
    
    public FinancialTransactionType Type { get; set; }
    public decimal Amount { get; set; } // Always positive, Type indicates direction

    /// <summary>
    /// True when this transaction represents an internal expense (rent, salaries, etc.)
    /// that should be counted under the "Internal expenses" bucket in financial reports.
    /// </summary>
    public bool IsInternalExpense { get; set; }

    /// <summary>
    /// Optional fine-grained classification for internal expenses.
    /// Only populated when <see cref="IsInternalExpense"/> is true.
    /// </summary>
    public InternalExpenseType? InternalExpenseType { get; set; }
    
    // Reference to related transaction
    public long? InventoryTransactionId { get; set; }
    public InventoryTransaction? InventoryTransaction { get; set; }
    
    public long? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }
    
    public int? ProductId { get; set; }
    public Product? Product { get; set; }
    
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? SupplierId { get; set; }
    public Supplier? Supplier { get; set; }

    public long? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    
    public long? PaymentRecordId { get; set; }
    public PaymentRecord? PaymentRecord { get; set; }
    
    public DateTimeOffset TimestampUtc { get; set; }
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string? Note { get; set; }
}
