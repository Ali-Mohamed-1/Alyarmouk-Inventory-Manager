namespace Inventory.Application.DTOs.StockSnapshot
{
    public record StockSnapshotResponseDto
    {
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;
        public string Sku { get; init; } = string.Empty;

        public decimal OnHand { get; init; }

        // Required for the UI to send back during updates to ensure concurrency
        public string RowVersion { get; init; } = string.Empty;
    }
}
