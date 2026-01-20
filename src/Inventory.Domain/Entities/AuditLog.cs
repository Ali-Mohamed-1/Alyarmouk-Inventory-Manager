namespace Inventory.Domain.Entities;

public enum AuditAction
{
    Create = 1,
    Update = 2,
    Delete = 3
}

public sealed class AuditLog
{
    public long Id { get; set; }

    public string EntityType { get; set; } = "";
    public string EntityId { get; set; } = "";
    public AuditAction Action { get; set; }

    public DateTimeOffset TimestampUtc { get; set; }
    public string UserId { get; set; } = "";
    public string UserDisplayName { get; set; } = "";

    // JSON snapshot of key fields before/after
    public string? ChangesJson { get; set; }
}
