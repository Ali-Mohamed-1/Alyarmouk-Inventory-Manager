using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.Product
{
    public record CreateBatchRequest
    {
        [Required]
        public int ProductId { get; init; }

        [Required]
        [StringLength(50)]
        public string BatchNumber { get; init; } = string.Empty;

        [Range(0, 10000000)]
        public decimal? UnitCost { get; init; }

        [Range(0, 10000000)]
        public decimal? UnitPrice { get; init; }

        [Range(0, 1000000)]
        public decimal InitialQuantity { get; init; }

        [StringLength(500)]
        public string? Notes { get; init; }
    }
}
