namespace Inventory.Application.DTOs;

public record CategoryResponseDto
{
	public int Id { get; init; }
	public string Name { get; init; } = string.Empty;
}
