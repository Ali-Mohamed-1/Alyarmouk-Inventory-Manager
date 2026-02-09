namespace Inventory.Application.DTOs.Reporting
{
    public record LowStockItemResponseDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        public decimal OnHand { get; init; }
        public string Unit { get; init; } = "pcs";
        public decimal ReorderPoint { get; init; }
    }
}
