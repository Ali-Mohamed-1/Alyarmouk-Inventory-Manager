namespace Inventory.Application.DTOs.SalesOrder
{
    public record SalesOrderResponseDto
    {
        public long Id { get; init; }
        public string OrderNumber { get; init; } = string.Empty;
        public int CustomerId { get; init; }
        public string CustomerName { get; init; } = string.Empty; // Mapped from CustomerNameSnapshot

        public DateTimeOffset CreatedUtc { get; init; }
        public string CreatedByUserDisplayName { get; init; } = string.Empty;
        public string? Note { get; init; }

        // Nested list of order items
        public List<SalesOrderLineResponseDto> Lines { get; init; } = new();
    }
}
