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
            var stockData = await _db.ProductBatches
                .AsNoTracking()
                .GroupBy(b => b.ProductId)
                .Select(g => new
                {
                    ProductId = g.Key,
                    OnHand = g.Sum(b => b.OnHand),
                    Reserved = g.Sum(b => b.Reserved)
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

            var stock = await _db.ProductBatches
                .AsNoTracking()
                .Where(b => b.ProductId == productId)
                .GroupBy(b => b.ProductId)
                .Select(g => new
                {
                    OnHand = g.Sum(b => b.OnHand),
                    Reserved = g.Sum(b => b.Reserved)
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

        public async Task UpdateAsync(UpdateStockRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.ProductId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Product ID must be positive.");

            if (req.NewQuantity < 0)
                throw new ValidationException("Stock quantity cannot be negative.");

            // Decode RowVersion for optimistic concurrency
            byte[] expectedRowVersion;
            try
            {
                expectedRowVersion = Convert.FromBase64String(req.RowVersion);
            }
            catch (FormatException)
            {
                throw new ValidationException("Invalid RowVersion format.");
            }

            // Verify product exists
            var productExists = await _db.Products
                .AsNoTracking()
                .AnyAsync(p => p.Id == req.ProductId, ct);

            if (!productExists)
                throw new NotFoundException($"Product id {req.ProductId} was not found.");

            // Load or create stock snapshot
            var snapshot = await _db.StockSnapshots
                .SingleOrDefaultAsync(s => s.ProductId == req.ProductId, ct);

            object? beforeState = null;
            var isNew = snapshot is null;
            
            if (isNew)
            {
                // Create new snapshot if it doesn't exist
                snapshot = new Inventory.Domain.Entities.StockSnapshot
                {
                    ProductId = req.ProductId,
                    OnHand = req.NewQuantity
                };
                _db.StockSnapshots.Add(snapshot);
            }
            else
            {
                // AUDIT LOG: Capture BEFORE state
                beforeState = new { OnHand = snapshot.OnHand };

                // Set original RowVersion for optimistic concurrency check
                _db.Entry(snapshot).Property(s => s.RowVersion).OriginalValue = expectedRowVersion;

                // Update the snapshot
                snapshot.OnHand = req.NewQuantity;
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Stock was modified by another user. Please refresh and try again.");
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update stock due to a database conflict.", ex);
            }
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
