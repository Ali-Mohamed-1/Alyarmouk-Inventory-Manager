namespace Inventory.Domain.Entities;

public enum InventoryTransactionType
{
    Receive = 1,
    Issue = 2,
    Adjust = 3
}

public sealed class InventoryTransaction
{
    public long Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    // Signed delta: Receive(+), Issue(-), Adjust(+/-)
    public decimal QuantityDelta { get; set; }

    public InventoryTransactionType Type { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";
    public string? Note { get; set; }
}


