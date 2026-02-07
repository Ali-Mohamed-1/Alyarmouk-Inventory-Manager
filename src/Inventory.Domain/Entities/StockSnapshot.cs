using System.ComponentModel.DataAnnotations.Schema;

namespace Inventory.Domain.Entities;

public class StockSnapshot
{
    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [NotMapped]
    public decimal OnHand { get; set; }
    [NotMapped]
    public decimal Reserved { get; set; }

    // Calculated property: Available = OnHand - Reserved
    [NotMapped]
    public decimal Available => OnHand - Reserved;
    
    // Optimistic concurrency for stock updates
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}