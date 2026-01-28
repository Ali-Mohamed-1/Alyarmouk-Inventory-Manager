namespace Inventory.Application.DTOs.Customer
{
    public record CustomerResponseDto
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? Phone { get; init; }
        public string? Email { get; init; }
        public string? Address { get; init; }
        public DateTimeOffset CreatedUtc { get; init; }
    }
}
