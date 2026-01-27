using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;

namespace Inventory.Application.Abstractions
{
    public interface ISalesOrderServices
    {
        /// <summary>
        /// Creates a new sales order
        /// </summary>
        Task<long> CreateAsync(CreateSalesOrderRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Retrieves a single sales order by id
        /// </summary>
        Task<SalesOrderResponseDto?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>
        /// Returns a customer's order history
        /// </summary>
        Task<IReadOnlyList<SalesOrderResponseDto>> GetCustomerOrdersAsync(int customerId, int take = 100, CancellationToken ct = default);

        /// <summary>
        /// Fetches the most recent orders
        /// </summary>
        Task<IReadOnlyList<SalesOrderResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default);

        /// <summary>
        /// Completes a sales order and records revenue
        /// </summary>
        Task CompleteOrderAsync(long orderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Updates the status of a sales order
        /// </summary>
        Task UpdateOrderStatusAsync(long orderId, UpdateSalesOrderStatusRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Attaches or updates a PDF file reference for an existing sales order.
        /// The caller is responsible for saving the actual file and providing its relative/absolute path.
        /// </summary>
        Task AttachPdfAsync(long orderId, string pdfPath, UserContext user, CancellationToken ct = default);

    }
}
