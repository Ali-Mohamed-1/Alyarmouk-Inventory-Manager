using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record CreateSalesOrderLineRequest
    {
        [Required]
        public int ProductId { get; init; }

        [Required]
        [Range(0.001, 1000000, ErrorMessage = "Quantity must be greater than zero.")]
        public decimal Quantity { get; init; }

        [Range(0, double.MaxValue, ErrorMessage = "Unit price cannot be negative.")]
        public decimal UnitPrice { get; init; }
    }
}
