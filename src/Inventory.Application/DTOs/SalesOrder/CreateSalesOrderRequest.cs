using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs.SalesOrder
{
    public record CreateSalesOrderRequest
    {
        [Required]
        public int CustomerId { get; init; }

        public string? Note { get; init; }

        [Required]
        [MinLength(1, ErrorMessage = "An order must have at least one line item.")]
        public List<CreateSalesOrderLineRequest> Lines { get; init; } = new();
    }
}
