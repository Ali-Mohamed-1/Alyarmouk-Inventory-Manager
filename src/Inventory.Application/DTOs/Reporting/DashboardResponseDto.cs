namespace Inventory.Application.DTOs.Reporting
{
    public record DashboardResponseDto
    {
        public int TotalProducts { get; init; }
        public decimal TotalOnHand { get; init; }
        public int LowStockCount { get; init; }
    }

}
