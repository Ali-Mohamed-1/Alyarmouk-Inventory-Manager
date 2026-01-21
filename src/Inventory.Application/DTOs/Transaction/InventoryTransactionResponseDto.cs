namespace Inventory.Application.DTOs.Transaction
{
    public record InventoryTransactionResponseDto
    {
        public long Id { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty;

        // Optional: Only populated if CustomerId exists
        public int? CustomerId { get; init; }
        public string? CustomerName { get; init; }

        public decimal QuantityDelta { get; init; }
        public string Type { get; init; } = string.Empty;

        public DateTimeOffset TimestampUtc { get; init; }
        public string UserDisplayName { get; init; } = string.Empty;
        public string? Note { get; init; }
    }
}
