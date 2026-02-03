using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;
using Inventory.Domain.Entities;

namespace Inventory.Application.Abstractions
{
    public interface IFinancialServices
    {
        /// <summary>
        /// Processes payment for a Sales Order when PaymentStatus becomes Paid.
        /// Increases revenue/cash, updates customer balance, and logs transaction.
        /// </summary>
        Task ProcessSalesPaymentAsync(long salesOrderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Processes payment for a Purchase Order when PaymentStatus becomes Paid.
        /// Increases accounts payable settlement, reduces cash/bank, updates supplier balance, and logs transaction.
        /// </summary>
        Task ProcessPurchasePaymentAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Reverses a payment for a Sales Order (e.g., Refund or status reversal).
        /// </summary>
        Task ReverseSalesPaymentAsync(long salesOrderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Reverses a payment for a Purchase Order.
        /// </summary>
        Task ReversePurchasePaymentAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default);

        Task ProcessSalesRefundPaymentAsync(long salesOrderId, decimal amount, UserContext user, CancellationToken ct = default);
        Task ProcessPurchaseRefundPaymentAsync(long purchaseOrderId, decimal amount, UserContext user, CancellationToken ct = default);
    }
}
