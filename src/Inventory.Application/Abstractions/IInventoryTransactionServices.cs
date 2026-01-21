using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Transaction;

namespace Inventory.Application.Abstractions
{
    public interface IInventoryTransactionServices
    {
        /// <summary>
        /// Creates a new inventory transaction record
        /// </summary>
        Task<long> CreateAsync(CreateInventoryTransactionRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Fetches the most recent inventory transactions
        /// </summary>
        Task<IReadOnlyList<InventoryTransactionResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default);

        /// <summary>
        /// Returns all transactions that involve the specified product
        /// </summary>
        Task<IReadOnlyList<InventoryTransactionResponseDto>> GetByProductAsync(int productId, CancellationToken ct = default);

        /// <summary>
        /// Lists transactions associated with a customer (for customer history)
        /// </summary>
        Task<IReadOnlyList<InventoryTransactionResponseDto>> GetTransactionsByCustomerAsync(int customerId, int take = 100, CancellationToken ct = default);
    }
}
