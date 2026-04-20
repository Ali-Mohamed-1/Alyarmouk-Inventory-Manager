using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Application.Exceptions;


namespace Inventory.Infrastructure.Services
{
    public sealed class StockSnapshotServices : IStockSnapshotServices
    {
        private readonly AppDbContext _db;

        public StockSnapshotServices(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<IReadOnlyList<StockSnapshotResponseDto>> GetAllAsync(CancellationToken ct = default)
        {
            var stockData = await _db.InventoryTransactions
                .AsNoTracking()
                .GroupBy(t => t.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    OnHand = g.Sum(t => t.QuantityDelta),
                    Reserved = 0m // Live reservation tracking removed from schema
                })
                .ToListAsync(ct);

            var stockByProductId = stockData.ToDictionary(x => x.ProductId);

            return await _db.StockSnapshots
                .AsNoTracking()
                .Include(s => s.Product)
                .OrderBy(s => s.Product != null ? s.Product.Name : string.Empty)
                .Select(s => new StockSnapshotResponseDto
                {
                    ProductId = s.ProductId,
                    ProductName = s.Product != null ? s.Product.Name : string.Empty,
                    Sku = s.Product != null ? s.Product.Sku : string.Empty,
                    OnHand = stockByProductId.ContainsKey(s.ProductId) ? stockByProductId[s.ProductId].OnHand : 0m,
                    Reserved = stockByProductId.ContainsKey(s.ProductId) ? stockByProductId[s.ProductId].Reserved : 0m,
                    Available = (stockByProductId.ContainsKey(s.ProductId) ? stockByProductId[s.ProductId].OnHand : 0m) - (stockByProductId.ContainsKey(s.ProductId) ? stockByProductId[s.ProductId].Reserved : 0m),
                    RowVersion = Convert.ToBase64String(s.RowVersion)
                })
                .ToListAsync(ct);
        }

        public async Task<StockSnapshotResponseDto?> GetByProductIdAsync(int productId, CancellationToken ct = default)
        {
            if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId), "Product ID must be positive.");

            var stock = await _db.InventoryTransactions
                .AsNoTracking()
                .Where(t => t.ProductId == productId)
                .GroupBy(t => t.ProductId)
                .Select(g => new
                {
                    OnHand = g.Sum(t => t.QuantityDelta),
                    Reserved = 0m // Live reservation tracking removed from schema
                })
                .FirstOrDefaultAsync(ct);

            return await _db.StockSnapshots
                .AsNoTracking()
                .Include(s => s.Product)
                .Where(s => s.ProductId == productId)
                .Select(s => new StockSnapshotResponseDto
                {
                    ProductId = s.ProductId,
                    ProductName = s.Product != null ? s.Product.Name : string.Empty,
                    Sku = s.Product != null ? s.Product.Sku : string.Empty,
                    OnHand = stock != null ? stock.OnHand : 0m,
                    Reserved = stock != null ? stock.Reserved : 0m,
                    Available = (stock != null ? stock.OnHand : 0m) - (stock != null ? stock.Reserved : 0m),
                    RowVersion = Convert.ToBase64String(s.RowVersion)
                })
                .SingleOrDefaultAsync(ct);
        }


        #region Helper Methods

        private static void ValidateUser(UserContext user)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.UserId)) throw new UnauthorizedAccessException("Missing user id.");
            if (string.IsNullOrWhiteSpace(user.UserDisplayName)) throw new UnauthorizedAccessException("Missing user display name.");
        }

        #endregion
    }
}
