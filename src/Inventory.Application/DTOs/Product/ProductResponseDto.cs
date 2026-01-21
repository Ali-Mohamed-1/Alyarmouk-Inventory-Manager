namespace Inventory.Application.DTOs.Product
{
    public record ProductResponseDto
    {
        public int Id { get; init; }
        public string Sku { get; init; } = string.Empty;
        public string Name { get; init; } = string.Empty;

        public int CategoryId { get; init; }
        public string CategoryName { get; init; } = string.Empty;

        public string Unit { get; init; } = "pcs";
        public decimal ReorderPoint { get; init; }
        public bool IsActive { get; init; }

        // Concurrency token converted to string for JSON compatibility
        public string RowVersion { get; init; } = string.Empty;
    }
}
