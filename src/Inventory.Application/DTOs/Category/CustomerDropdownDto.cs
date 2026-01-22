namespace Inventory.Application.DTOs.Customer
{
    public record CustomerDropdownDto
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }
}