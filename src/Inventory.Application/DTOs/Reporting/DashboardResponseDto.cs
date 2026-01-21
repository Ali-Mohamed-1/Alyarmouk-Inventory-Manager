namespace Inventory.Application.DTOs.Reporting
{
    public record DashboardResponseDto
    {
        public int TotalProducts { get; init; }
        public decimal TotalOnHand { get; init; }
        public int LowStockCount { get; init; }

        public IReadOnlyList<DashboardStockByCategoryPointDto> StockByCategory { get; init; } =
            Array.Empty<DashboardStockByCategoryPointDto>();
    }

    public record DashboardStockByCategoryPointDto
    {
        public string CategoryName { get; init; } = string.Empty;
        public decimal OnHand { get; init; }
    }
}
