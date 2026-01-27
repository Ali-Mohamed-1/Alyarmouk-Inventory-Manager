using System;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record CustomerBalanceResponseDto
    {
        public int CustomerId { get; init; }

        /// <summary>
        /// Sum of all open (unpaid) sales orders for this customer, regardless of due date.
        /// </summary>
        public decimal TotalPending { get; init; }

        /// <summary>
        /// Subset of TotalPending that is already due or overdue as of AsOfUtc
        /// (i.e. PaymentStatus = Pending and DueDate &lt;= AsOfUtc).
        /// This is effectively "how much the customer owes us now".
        /// </summary>
        public decimal TotalDueNow { get; init; }

        /// <summary>
        /// Balance calculation timestamp (UTC).
        /// </summary>
        public DateTimeOffset AsOfUtc { get; init; }
    }
}
