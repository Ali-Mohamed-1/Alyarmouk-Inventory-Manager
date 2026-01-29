using Inventory.Domain.Entities;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs.Transaction
{
    public record CreateInventoryTransactionRequest
    {
        [Required]
        public int ProductId { get; init; }

        // Optional: Used mainly for 'Issue' (sales)
        public int? CustomerId { get; init; }

        [Required]
        public decimal Quantity { get; init; }

        [Required]
        public InventoryTransactionType Type { get; init; }

        [StringLength(100)]
        public string? BatchNumber { get; init; }

        [StringLength(500)]
        public string? Note { get; init; }
    }
}
