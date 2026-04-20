namespace Inventory.Domain.Entities;

/// <summary>
/// Stores per-batch metadata (cost/price/notes) for a product.
/// Quantity on-hand per batch is derived from inventory transactions (not stored here).
/// </summary>
public sealed class ProductBatch
{
    public long Id { get; set; }
    public int ProductId { get; set; }
    public string BatchNumber { get; set; } = string.Empty;

    public decimal? UnitCost { get; set; }
    public decimal? UnitPrice { get; set; }
    public string? Notes { get; set; }

    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    // Optimistic concurrency
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();
}

