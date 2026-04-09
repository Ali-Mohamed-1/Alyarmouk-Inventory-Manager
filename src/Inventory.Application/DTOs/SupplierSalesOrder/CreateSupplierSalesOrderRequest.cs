using System.ComponentModel.DataAnnotations;
using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs.SupplierSalesOrder
{
    public record CreateSupplierSalesOrderRequest
    {
        [Required]
        public int SupplierId { get; init; }

        public string? Note { get; init; }

        /// <summary>
        /// Order business date. Defaults to now if not supplied.
        /// </summary>
        public DateTimeOffset? OrderDate { get; init; }

        /// <summary>
        /// When the supplier is expected to pay.
        /// </summary>
        [Required]
        public DateTimeOffset? DueDate { get; init; }

        /// <summary>
        /// Payment method for this order (cash / check / bank transfer).
        /// </summary>
        [Required]
        public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;

        /// <summary>
        /// Initial payment status. RecalculatePaymentStatus() will be called after creation.
        /// </summary>
        public PaymentStatus PaymentStatus { get; init; } = PaymentStatus.Pending;

        // Check details (nullable)
        public bool CheckReceived { get; init; } = false;
        public DateTimeOffset? CheckReceivedDate { get; init; }
        public bool CheckCashed { get; init; } = false;
        public DateTimeOffset? CheckCashedDate { get; init; }
        
        /// <summary>
        /// For bank transfer payments.
        /// </summary>
        public string? TransferId { get; init; }

        // Tax Configuration
        public bool IsTaxInclusive { get; init; } = false;
        public bool ApplyVat { get; init; } = true;
        public bool ApplyManufacturingTax { get; init; } = true;

        /// <summary>
        /// Indicates if this order is a historical record.
        /// </summary>
        public bool IsHistorical { get; init; }

        /// <summary>
        /// Explicitly set status (e.g. for historical orders). Defaults to Pending.
        /// </summary>
        public SalesOrderStatus? Status { get; init; }

        [Required]
        [MinLength(1, ErrorMessage = "An order must have at least one line item.")]
        public List<CreateSupplierSalesOrderLineRequest> Lines { get; init; } = new();
    }

    public record CreateSupplierSalesOrderLineRequest
    {
        [Required]
        public int ProductId { get; init; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Quantity must be greater than zero.")]
        public decimal Quantity { get; init; }

        [Required]
        [Range(0, double.MaxValue, ErrorMessage = "Unit price cannot be negative.")]
        public decimal UnitPrice { get; init; }

        /// <summary>
        /// Optional batch/lot number.
        /// </summary>
        public string? BatchNumber { get; init; }

        /// <summary>
        /// Optional specific batch ID.
        /// </summary>
        public long? ProductBatchId { get; init; }
    }
}
