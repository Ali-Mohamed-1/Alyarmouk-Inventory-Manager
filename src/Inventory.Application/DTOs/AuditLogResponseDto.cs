using Inventory.Domain.Entities;

namespace Inventory.Application.DTOs;

public record AuditLogResponseDto
{
    public long Id { get; init; }

    public string EntityType { get; init; } = string.Empty;
    public string EntityId { get; init; } = string.Empty;

    public string Action { get; init; }

    public DateTimeOffset TimestampUtc { get; init; }

    public string UserDisplayName { get; init; } = string.Empty;

    public string? ChangesJson { get; init; }
}