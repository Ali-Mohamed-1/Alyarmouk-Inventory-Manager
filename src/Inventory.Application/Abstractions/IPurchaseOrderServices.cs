using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Entities;

namespace Inventory.Application.Abstractions
{
    public interface IPurchaseOrderServices
    {
        Task<IEnumerable<PurchaseOrderResponse>> GetRecentAsync(int count = 10, CancellationToken ct = default);
        Task<PurchaseOrderResponse?> GetByIdAsync(long id, CancellationToken ct = default);
        Task<IEnumerable<PurchaseOrderResponse>> GetBySupplierAsync(int supplierId, CancellationToken ct = default);
        Task<long> CreateAsync(CreatePurchaseOrderRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Cancels a purchase order.
        /// Does NOT perform any automatic stock or payment refunds.
        /// Cancellation is only allowed when all refunds have been processed manually.
        /// </summary>
        Task CancelAsync(long id, UserContext user, CancellationToken ct = default);

        Task UpdateStatusAsync(long id, PurchaseOrderStatus status, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default);
        Task UpdatePaymentDeadlineAsync(long id, DateTimeOffset? newDeadline, UserContext user, CancellationToken ct = default);
        Task RefundAsync(RefundPurchaseOrderRequest req, UserContext user, CancellationToken ct = default);
        Task AddPaymentAsync(long orderId, Inventory.Application.DTOs.Payment.CreatePaymentRequest req, UserContext user, CancellationToken ct = default);
        
        /// <summary>
        /// Attaches or updates an Invoice PDF file reference for an existing purchase order.
        /// </summary>
        Task AttachInvoiceAsync(long orderId, string invoicePath, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Removes the Invoice PDF file reference from a purchase order.
        /// </summary>
        Task RemoveInvoiceAsync(long orderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Attaches or updates a Receipt PDF file reference for an existing purchase order.
        /// </summary>
        Task AttachReceiptAsync(long orderId, string receiptPath, UserContext user, CancellationToken ct = default);

        Task RemoveReceiptAsync(long orderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Manually triggers stock reception for a historical order that was created without stock impact.
        /// </summary>
        Task ActivateStockAsync(long orderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Updates payment metadata for an existing purchase order.
        /// </summary>
        Task UpdatePaymentInfoAsync(long id, UpdatePurchaseOrderPaymentRequest req, UserContext user, CancellationToken ct = default);
    }
}
