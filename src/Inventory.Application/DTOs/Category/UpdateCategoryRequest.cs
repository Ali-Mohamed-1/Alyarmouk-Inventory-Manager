using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs;

public record UpdateCategoryRequest
{
    [Required]
    public int Id { get; init; }

    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string Name { get; init; } = string.Empty;
}