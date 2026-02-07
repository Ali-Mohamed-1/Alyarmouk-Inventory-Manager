using System.ComponentModel.DataAnnotations;

namespace Inventory.Application.DTOs.Batches;

public sealed record UpsertProductBatchRequestDto
{
    [Range(0, 100000000)]
    public decimal? UnitCost { get; init; }

    [Range(0, 100000000)]
    public decimal? UnitPrice { get; init; }

    [StringLength(500)]
    public string? Notes { get; init; }

    // optimistic concurrency for ProductBatch row
    public string? RowVersion { get; init; }
}

