using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs;

public record CreateCategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;
}