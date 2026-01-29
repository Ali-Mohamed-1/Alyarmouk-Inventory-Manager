namespace Inventory.Application.DTOs.Batches;

public sealed record ProductBatchResponseDto
{
    public string BatchNumber { get; init; } = string.Empty;
    public bool IsUnbatched { get; init; }

    public decimal OnHand { get; init; }
    public decimal? UnitCost { get; init; }
    public decimal? UnitPrice { get; init; }

    public DateTimeOffset? LastMovementUtc { get; init; }
    public string? Notes { get; init; }
    public string RowVersion { get; init; } = string.Empty;
}

