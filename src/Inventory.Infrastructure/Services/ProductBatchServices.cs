using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs.Batches;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services;

public sealed class ProductBatchServices : IProductBatchServices
{
    private readonly AppDbContext _db;

    public ProductBatchServices(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<ProductBatchResponseDto>> GetForProductAsync(int productId, CancellationToken ct = default)
    {
        if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId), "Product ID must be positive.");

        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Cost, p.Price })
            .FirstOrDefaultAsync(ct);

        if (product is null)
            return Array.Empty<ProductBatchResponseDto>();

        // Execute each query completely before starting the next one
        var poBatches = await _db.PurchaseOrderLines
            .AsNoTracking()
            .Where(l => l.ProductId == productId && l.BatchNumber != null && l.BatchNumber != "")
            .Select(l => l.BatchNumber!)
            .Distinct()
            .ToListAsync(ct);

        var soBatches = await _db.SalesOrderLines
            .AsNoTracking()
            .Where(l => l.ProductId == productId && l.BatchNumber != null && l.BatchNumber != "")
            .Select(l => l.BatchNumber!)
            .Distinct()
            .ToListAsync(ct);

        var txBatches = await _db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ProductId == productId && t.BatchNumber != null && t.BatchNumber != "")
            .Select(t => t.BatchNumber!)
            .Distinct()
            .ToListAsync(ct);

        var hasUnbatched = await _db.InventoryTransactions
            .AsNoTracking()
            .AnyAsync(t => t.ProductId == productId && (t.BatchNumber == null || t.BatchNumber == ""), ct);

        var allBatchNumbers = poBatches
            .Concat(soBatches)
            .Concat(txBatches)
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Get transaction aggregates per batch
        // These are the actual inventory quantities (source of truth)
        var txAgg = await _db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ProductId == productId)
            .GroupBy(t => (t.BatchNumber ?? "").Trim())
            .Select(g => new
            {
                BatchNumber = g.Key,
                OnHand = g.Sum(x => x.QuantityDelta),
                LastMovementUtc = g.Max(x => x.TimestampUtc),
                LastUnitCost = g.OrderByDescending(x => x.TimestampUtc).Select(x => x.UnitCost).FirstOrDefault()
            })
            .ToListAsync(ct);

        var txAggByBatch = txAgg.ToDictionary(x => x.BatchNumber, x => x, StringComparer.OrdinalIgnoreCase);

        // Get batch metadata
        var metas = await _db.ProductBatches
            .AsNoTracking()
            .Where(b => b.ProductId == productId)
            .ToListAsync(ct);

        var metaByBatch = metas.ToDictionary(x => x.BatchNumber, x => x, StringComparer.OrdinalIgnoreCase);

        var result = new List<ProductBatchResponseDto>();

        // Unbatched
        if (hasUnbatched)
        {
            var key = "";
            txAggByBatch.TryGetValue(key, out var unbatchedAgg);
            metaByBatch.TryGetValue(key, out var unbatchedMeta);

            result.Add(new ProductBatchResponseDto
            {
                Id = unbatchedMeta?.Id ?? 0,
                BatchNumber = "Unbatched",
                IsUnbatched = true,
                OnHand = unbatchedAgg?.OnHand ?? 0m,
                UnitCost = unbatchedMeta?.UnitCost ?? unbatchedAgg?.LastUnitCost ?? product.Cost,
                UnitPrice = unbatchedMeta?.UnitPrice ?? product.Price,
                LastMovementUtc = unbatchedAgg?.LastMovementUtc,
                Notes = unbatchedMeta?.Notes,
                RowVersion = unbatchedMeta?.RowVersion is { Length: > 0 }
                    ? Convert.ToBase64String(unbatchedMeta.RowVersion)
                    : string.Empty
            });
        }

        foreach (var batchNumber in allBatchNumbers)
        {
            txAggByBatch.TryGetValue(batchNumber, out var agg);
            metaByBatch.TryGetValue(batchNumber, out var meta);

            result.Add(new ProductBatchResponseDto
            {
                Id = meta?.Id ?? 0,
                BatchNumber = batchNumber,
                IsUnbatched = false,
                OnHand = agg?.OnHand ?? 0m,
                UnitCost = meta?.UnitCost ?? agg?.LastUnitCost ?? product.Cost,
                UnitPrice = meta?.UnitPrice ?? product.Price,
                LastMovementUtc = agg?.LastMovementUtc,
                Notes = meta?.Notes,
                RowVersion = meta?.RowVersion is { Length: > 0 }
                    ? Convert.ToBase64String(meta.RowVersion)
                    : string.Empty
            });
        }

        return result;
    }

    public async Task<ProductBatchResponseDto> UpsertAsync(
        int productId,
        string batchNumber,
        UpsertProductBatchRequestDto request,
        CancellationToken ct = default)
    {
        if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId), "Product ID must be positive.");
        if (batchNumber is null) throw new ArgumentNullException(nameof(batchNumber));

        var normalized = batchNumber.Trim();
        if (string.Equals(normalized, "Unbatched", StringComparison.OrdinalIgnoreCase))
            normalized = "";

        var product = await _db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.Id, p.Cost, p.Price })
            .FirstOrDefaultAsync(ct);
        if (product is null)
            throw new InvalidOperationException($"Product id {productId} not found.");

        var entity = await _db.ProductBatches
            .FirstOrDefaultAsync(b => b.ProductId == productId && b.BatchNumber == normalized, ct);

        if (entity is null)
        {
            entity = new Domain.Entities.ProductBatch
            {
                ProductId = productId,
                BatchNumber = normalized
            };
            _db.ProductBatches.Add(entity);
        }
        else if (!string.IsNullOrWhiteSpace(request.RowVersion))
        {
            var bytes = Convert.FromBase64String(request.RowVersion);
            _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = bytes;
        }

        entity.UnitCost = request.UnitCost;
        entity.UnitPrice = request.UnitPrice;
        entity.Notes = request.Notes;
        entity.UpdatedUtc = DateTimeOffset.UtcNow;

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException("Batch was updated by another user.", ex);
        }

        // recompute aggregates for this single batch
        var key = normalized;
        var agg = await _db.InventoryTransactions
            .AsNoTracking()
            .Where(t => t.ProductId == productId && ((t.BatchNumber ?? "").Trim() == key))
            .GroupBy(t => 1)
            .Select(g => new
            {
                OnHand = g.Sum(x => x.QuantityDelta),
                LastMovementUtc = g.Max(x => x.TimestampUtc),
                LastUnitCost = g.OrderByDescending(x => x.TimestampUtc).Select(x => x.UnitCost).FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        var displayBatch = string.IsNullOrEmpty(normalized) ? "Unbatched" : normalized;

        return new ProductBatchResponseDto
        {
            Id = entity.Id,
            BatchNumber = displayBatch,
            IsUnbatched = string.IsNullOrEmpty(normalized),
            OnHand = agg?.OnHand ?? 0m,
            UnitCost = entity.UnitCost ?? agg?.LastUnitCost ?? product.Cost,
            UnitPrice = entity.UnitPrice ?? product.Price,
            LastMovementUtc = agg?.LastMovementUtc,
            Notes = entity.Notes,
            RowVersion = entity.RowVersion is { Length: > 0 }
                ? Convert.ToBase64String(entity.RowVersion)
                : string.Empty
        };
    }
}