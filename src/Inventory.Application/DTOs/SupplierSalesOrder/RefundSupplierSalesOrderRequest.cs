using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.SupplierSalesOrder
{
    public record RefundSupplierSalesOrderRequest
    {
        [Required]
        public long SupplierSalesOrderId { get; init; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Refund amount must be positive.")]
        public decimal Amount { get; init; }

        [Required]
        public string Reason { get; init; } = "";

        /// <summary>
        /// Optional: List of product lines to refund quantity for.
        /// If provided, stock will be returned to inventory.
        /// </summary>
        public List<RefundSupplierSalesOrderLineRequest>? LineItems { get; init; }
    }

    public record RefundSupplierSalesOrderLineRequest
    {
        [Required]
        public long SupplierSalesOrderLineId { get; init; }

        [Required]
        [Range(0.01, double.MaxValue, ErrorMessage = "Refund quantity must be positive.")]
        public decimal Quantity { get; init; }

        public string? BatchNumber { get; init; }
        public long? ProductBatchId { get; init; }
    }
}
