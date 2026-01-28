using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.Product
{
    public record CreateProductRequest
    {
        [Required]
        [StringLength(50)]
        public string Sku { get; init; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; init; } = string.Empty;

        [Required]
        public int CategoryId { get; init; }

        [Range(0, 100000)]
        [Required]
        public decimal Price { get; init; }

        public decimal Cost { get; init; }

        public string Unit { get; init; } = "pcs";

        [Range(0, 1000000)]
        public decimal ReorderPoint { get; init; }
        public bool isActive { get; init; }
        public string RowVersion { get; init; } = string.Empty;
    }
}
