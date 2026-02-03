using System;

namespace Inventory.Application.DTOs.PurchaseOrder
{
    public record SupplierBalanceResponseDto
    {
        public int SupplierId { get; init; }
        public string SupplierName { get; init; } = string.Empty;

        /// <summary>
        /// Total value of all non-cancelled purchase orders (minus refunds).
        /// </summary>
        public decimal TotalOrders { get; init; }
        
        /// <summary>
        /// Total payments made to this supplier.
        /// </summary>
        public decimal TotalPayments { get; init; }

        /// <summary>
        /// Net balance owed to supplier (TotalOrders - TotalPayments).
        /// </summary>
        public decimal Balance => TotalOrders - TotalPayments;

        /// <summary>
        /// Sum of all unpaid/partially paid purchase orders, regardless of deadline.
        /// </summary>
        public decimal TotalPending { get; init; }

        /// <summary>
        /// Subset of TotalPending where PaymentDeadline has passed.
        /// This is the "Deserved" balance - what we owe them NOW.
        /// </summary>
        public decimal Deserved { get; init; }

        public DateTimeOffset AsOfUtc { get; init; }
    }
}
