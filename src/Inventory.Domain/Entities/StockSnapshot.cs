namespace Inventory.Domain.Entities;

public sealed class StockSnapshot
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal OnHand { get; set; }

    // Optimistic concurrency for stock updates
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}


