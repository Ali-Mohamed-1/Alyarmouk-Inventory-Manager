using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Domain.Entities;

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
        Task CompleteOrderAsync(long orderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default);

        /// <summary>
        /// Cancels a sales order.
        /// Does NOT perform any automatic stock or payment refunds.
        /// Cancellation is only allowed when all refunds have been processed manually.
        /// </summary>
        Task CancelAsync(long orderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Updates the status of a sales order
        /// </summary>
        Task UpdateStatusAsync(long orderId, UpdateSalesOrderStatusRequest req, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default);
        Task UpdateDueDateAsync(long orderId, DateTimeOffset newDate, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Attaches or updates an Invoice PDF file reference for an existing sales order.
        /// The caller is responsible for saving the actual file and providing its relative/absolute path.
        /// </summary>
        Task AttachInvoiceAsync(long orderId, string invoicePath, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Removes the Invoice PDF file reference from a sales order.
        /// </summary>
        Task RemoveInvoiceAsync(long orderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Attaches or updates a Receipt PDF file reference for an existing sales order.
        /// </summary>
        Task AttachReceiptAsync(long orderId, string receiptPath, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Removes the Receipt PDF file reference from a sales order.
        /// </summary>
        Task RemoveReceiptAsync(long orderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Updates the payment information including status and check details.
        /// </summary>
        Task UpdatePaymentInfoAsync(long orderId, UpdateSalesOrderPaymentRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Processes a refund for a completed sales order.
        /// Reverses revenue, COGS, and restores inventory.
        /// </summary>
        Task RefundAsync(RefundSalesOrderRequest req, UserContext user, CancellationToken ct = default);

        Task AddPaymentAsync(long orderId, Inventory.Application.DTOs.Payment.CreatePaymentRequest req, UserContext user, CancellationToken ct = default);
        
        /// <summary>
        /// Manually triggers stock deduction for a historical order that was created without stock impact.
        /// </summary>
        Task ActivateStockAsync(long orderId, UserContext user, CancellationToken ct = default);
    }
}
