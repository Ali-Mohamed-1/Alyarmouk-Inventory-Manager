using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record CreateSalesOrderRequest
    {
        [Required]
        public int CustomerId { get; init; }

        public string? Note { get; init; }

        /// <summary>
        /// Order business date. Defaults to now if not supplied.
        /// </summary>
        public DateTimeOffset? OrderDate { get; init; }

        /// <summary>
        /// When the customer is expected to pay.
        /// </summary>
        [Required]
        public DateTimeOffset? DueDate { get; init; }

        /// <summary>
        /// Payment method for this order (cash / check).
        /// </summary>
        [Required]
        public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;

        /// <summary>
        /// Overall payment status.
        /// </summary>
        public PaymentStatus PaymentStatus { get; init; } = PaymentStatus.Pending;

        /// <summary>
        /// For check payments: whether we received the check.
        /// Ignored for cash payments.
        /// </summary>
        public bool? CheckReceived { get; init; }

        /// <summary>
        /// For check payments: date when the check was received.
        /// </summary>
        public DateTimeOffset? CheckReceivedDate { get; init; }

        /// <summary>
        /// For check payments: whether the check has been cashed.
        /// </summary>
        public bool? CheckCashed { get; init; }

        /// <summary>
        /// For check payments: date when the check was cashed.
        /// </summary>
        public DateTimeOffset? CheckCashedDate { get; init; }

        // Tax Configuration
        public bool IsTaxInclusive { get; init; } = true;
        public bool ApplyVat { get; init; } = true;
        public bool ApplyManufacturingTax { get; init; } = true;

        [Required]
        [MinLength(1, ErrorMessage = "An order must have at least one line item.")]
        public List<CreateSalesOrderLineRequest> Lines { get; init; } = new();
    }
}
