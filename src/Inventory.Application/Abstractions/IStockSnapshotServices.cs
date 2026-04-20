using Inventory.Application.DTOs;
using Inventory.Application.DTOs.StockSnapshot;

namespace Inventory.Application.Abstractions
{
    public interface IStockSnapshotServices
    {
        /// <summary>
        /// Retrieves all stock snapshots
        /// </summary>
        Task<IReadOnlyList<StockSnapshotResponseDto>> GetAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Gets the snapshot for a single product
        /// </summary>
        Task<StockSnapshotResponseDto?> GetByProductIdAsync(int productId, CancellationToken ct = default);

    }
}
