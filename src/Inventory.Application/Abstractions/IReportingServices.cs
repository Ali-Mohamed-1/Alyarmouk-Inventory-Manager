using Inventory.Application.DTOs.Reporting;

namespace Inventory.Application.Abstractions
{
    public interface IReportingServices
    {
        /// <summary>
        /// Gathers the key dashboard metrics
        /// </summary>
        Task<DashboardResponseDto> GetDashboardAsync(CancellationToken ct = default);

        /// <summary>
        /// Returns items that are running low so teams can restock early
        /// </summary>
        Task<IReadOnlyList<LowStockItemResponseDto>> GetLowStockAsync(CancellationToken ct = default);
    }
}
