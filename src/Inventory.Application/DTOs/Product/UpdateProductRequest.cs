using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs.Product
{
    public record UpdateProductRequest
    {
        [Required]
        public int Id { get; init; }

        [Required]
        public string Sku { get; init; } = string.Empty;

        [Required]
        public string Name { get; init; } = string.Empty;



        public int CategoryId { get; init; }
        public string Unit { get; init; } = "pcs";
        public decimal ReorderPoint { get; init; }
        public bool IsActive { get; init; }

        // Must be sent back to the server to verify the version
        [Required]
        public string RowVersion { get; init; } = string.Empty;
    }
}
