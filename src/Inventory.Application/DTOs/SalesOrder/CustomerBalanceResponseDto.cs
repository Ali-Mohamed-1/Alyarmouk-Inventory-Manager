using System;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record CustomerBalanceResponseDto
    {
        public int CustomerId { get; init; }

        /// <summary>
        /// Sum of all open (unpaid/partially paid) sales orders for this customer, regardless of due date.
        /// </summary>
        public decimal TotalPending { get; init; }

        /// <summary>
        /// Subset of TotalPending that is already overdue as of AsOfUtc
        /// (i.e. PaymentStatus != Paid and DueDate &lt; AsOfUtc).
        /// This is the "Deserved" balance - what the customer owes us NOW.
        /// </summary>
        public decimal Deserved { get; init; }

        /// <summary>
        /// Balance calculation timestamp (UTC).
        /// </summary>
        public DateTimeOffset AsOfUtc { get; init; }
    }
}

