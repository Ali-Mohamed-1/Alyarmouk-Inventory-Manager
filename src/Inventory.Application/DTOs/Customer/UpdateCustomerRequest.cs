using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Inventory.Application.DTOs.Customer
{
    public record UpdateCustomerRequest
    {
        [Required]
        public int Id { get; init; }

        [Required]
        [StringLength(200)]
        public string Name { get; init; } = string.Empty;

        [Phone]
        public string? Phone { get; init; }

        [EmailAddress]
        public string? Email { get; init; }

        public string? Address { get; init; }
    }
}
