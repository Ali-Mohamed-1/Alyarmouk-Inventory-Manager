namespace Inventory.Domain.Entities;

public sealed class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";

    public int CategoryId { get; set; }
    public Category? Category { get; set; }

    public string Unit { get; set; } = "pcs";
    public decimal ReorderPoint { get; set; }
    
    // Financial fields
    public decimal Cost { get; set; } // Cost per unit (for inventory valuation)
    public decimal Price { get; set; } // Selling price per unit

    public bool IsActive { get; set; } = true;

    // Optimistic concurrency
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}


