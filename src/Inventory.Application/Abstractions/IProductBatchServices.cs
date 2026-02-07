using Inventory.Application.DTOs.Batches;

namespace Inventory.Application.Abstractions;

public interface IProductBatchServices
{
    /// <summary>
    /// Returns all batches for a given product, including on-hand quantity
    /// (derived from inventory transactions), last movement date, and any
    /// stored per-batch metadata (cost/price/notes).
    /// </summary>
    Task<IReadOnlyList<ProductBatchResponseDto>> GetForProductAsync(int productId, CancellationToken ct = default);

    /// <summary>
    /// Updates or creates per-batch metadata (cost, price, notes) for a
    /// specific product + batch combination.
    /// </summary>
    Task<ProductBatchResponseDto> UpsertAsync(
        int productId,
        string batchNumber,
        UpsertProductBatchRequestDto request,
        CancellationToken ct = default);
}

