using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs.StockSnapshot
{
    public record UpdateStockRequest
    {
        [Required]
        public int ProductId { get; init; }

        [Required]
        public decimal NewQuantity { get; init; }

        // To prevent overwriting another person's update
        [Required]
        public string RowVersion { get; init; } = string.Empty;
    }
}
