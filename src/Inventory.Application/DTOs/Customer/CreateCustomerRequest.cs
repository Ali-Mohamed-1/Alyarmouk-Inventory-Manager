using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.Customer
{
    public record CreateCustomerRequest
    {
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
