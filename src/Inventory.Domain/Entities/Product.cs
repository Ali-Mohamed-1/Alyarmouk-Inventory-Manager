namespace Inventory.Domain.Entities;

public sealed class Product
{
    public int Id { get; set; }
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";

    public string Unit { get; set; } = "pcs";
    public decimal ReorderPoint { get; set; }
    


    public bool IsActive { get; set; } = true;

    // Optimistic concurrency
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public ICollection<Supplier> Suppliers { get; set; } = new List<Supplier>();
}


