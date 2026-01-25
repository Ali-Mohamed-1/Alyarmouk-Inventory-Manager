namespace Inventory.Domain.Entities;

public class StockSnapshot
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public decimal OnHand { get; set; }
    public decimal Preserved { get; set; }
    
    // Available = OnHand - Preserved (calculated property)
    public decimal Available => OnHand - Preserved;

    // Optimistic concurrency for stock updates
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}