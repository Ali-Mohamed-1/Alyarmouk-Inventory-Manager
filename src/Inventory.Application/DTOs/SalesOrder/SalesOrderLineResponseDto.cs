namespace Inventory.Application.DTOs.SalesOrder
{
    public record SalesOrderLineResponseDto
    {
        public long Id { get; init; }
        public int ProductId { get; init; }
        public string ProductName { get; init; } = string.Empty; // Mapped from ProductNameSnapshot
        public decimal Quantity { get; init; }
        public string Unit { get; init; } = string.Empty; // Mapped from UnitSnapshot
        public decimal UnitPrice { get; init; }
        public string? BatchNumber { get; init; }
        public long? ProductBatchId { get; init; }
        public decimal LineSubtotal { get; init; }
        public decimal LineVatAmount { get; init; }
        public decimal LineManufacturingTaxAmount { get; init; }
        public decimal LineTotal { get; init; }
    }
}
