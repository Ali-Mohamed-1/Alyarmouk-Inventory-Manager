using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services
{
    public sealed class InventoryTransactionServices : IInventoryTransactionServices
    {
        private const int MaxTake = 1000;
        private readonly AppDbContext _db;
        private readonly IAuditLogWriter _auditWriter;

        public InventoryTransactionServices(AppDbContext db, IAuditLogWriter auditWriter)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        }

        public async Task<long> CreateAsync(CreateInventoryTransactionRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.ProductId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Product ID must be positive.");

            if (req.Quantity <= 0)
                throw new ValidationException("Quantity must be greater than zero.");

            // Verify product exists and is active
            var product = await _db.Products
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == req.ProductId, ct);

            if (product is null)
                throw new NotFoundException($"Product id {req.ProductId} was not found.");

            if (!product.IsActive)
                throw new ValidationException("Cannot create transaction for inactive product.");

            // Verify customer exists if provided
            if (req.CustomerId.HasValue && req.CustomerId.Value > 0)
            {
                var customerExists = await _db.Customers
                    .AsNoTracking()
                    .AnyAsync(c => c.Id == req.CustomerId.Value, ct);

                if (!customerExists)
                    throw new NotFoundException($"Customer id {req.CustomerId.Value} was not found.");
            }

            // Calculate signed delta based on transaction type
            decimal quantityDelta = req.Type switch
            {
                InventoryTransactionType.Receive => req.Quantity,  // Positive
                InventoryTransactionType.Issue => -req.Quantity,  // Negative
                InventoryTransactionType.Adjust => req.Quantity,   // Can be positive or negative, but DTO only provides positive
                _ => throw new ValidationException($"Invalid transaction type: {req.Type}")
            };

            // Use transaction to ensure stock snapshot and transaction record are updated atomically
            // Check if we already have an active transaction (e.g. from SalesOrderServices.RefundAsync)
            var existingTransaction = _db.Database.CurrentTransaction;
            var transaction = existingTransaction == null ? await _db.Database.BeginTransactionAsync(ct) : null;
            
            try
            {
                // 1. Ensure Product exists (already checked above, but keeping for consistency with provided snippet structure)
                // var productExists = await _db.Products.AnyAsync(p => p.Id == req.ProductId, ct);
                // if (!productExists)
                //     throw new NotFoundException($"Product id {req.ProductId} was not found.");

                // 2. Load or create StockSnapshot (mostly for RowVersion/existence)
                var snapshot = await _db.StockSnapshots
                    .FirstOrDefaultAsync(s => s.ProductId == req.ProductId, ct);

                if (snapshot is null)
                {
                    snapshot = new StockSnapshot { ProductId = req.ProductId, OnHand = 0 }; // Initialize OnHand
                    _db.StockSnapshots.Add(snapshot);
                }

                // 3. Ensure ProductBatch exists
                var batchNumber = (req.BatchNumber ?? "").Trim();
                ProductBatch? batch = null;

                if (req.ProductBatchId.HasValue && req.ProductBatchId.Value > 0)
                {
                    batch = await _db.ProductBatches.FindAsync(new object[] { req.ProductBatchId.Value }, ct);
                }
                
                if (batch == null)
                {
                    batch = await _db.ProductBatches.FirstOrDefaultAsync(b => b.ProductId == req.ProductId && b.BatchNumber == batchNumber, ct);
                }

                if (batch == null)
                {
                    batch = new ProductBatch
                    {
                        ProductId = req.ProductId,
                        BatchNumber = batchNumber,
                        OnHand = 0,
                        Reserved = 0
                    };
                    _db.ProductBatches.Add(batch);
                    // No need to SaveChanges here, it will be saved with the transaction and other entities
                }

                // 4. Validation
                if (req.Type == InventoryTransactionType.Issue)
                {
                    if (batch.OnHand + quantityDelta < 0)
                        throw new ValidationException($"Insufficient stock in batch '{batchNumber}'. Available: {batch.OnHand}, Requested: {req.Quantity}");
                }

                // 5. Create transaction
                var inventoryTransaction = new InventoryTransaction
                {
                    ProductId = req.ProductId,
                    QuantityDelta = quantityDelta,
                    UnitCost = null, // Derived from batch logic elsewhere or added later
                    Type = req.Type,
                    BatchNumber = batchNumber,
                    ProductBatchId = batch.Id,
                    Note = req.Note,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = user.UserId,
                    UserDisplayName = user.UserDisplayName,
                    clientId = req.CustomerId ?? 0
                };

                // Update batch OnHand
                batch.OnHand += quantityDelta;

                _db.InventoryTransactions.Add(inventoryTransaction);
                await _db.SaveChangesAsync(ct); 

                await _auditWriter.LogCreateAsync<InventoryTransaction>(
                    inventoryTransaction.Id,
                    user,
                    afterState: new
                    {
                        ProductId = inventoryTransaction.ProductId,
                        QuantityDelta = inventoryTransaction.QuantityDelta,
                        Type = inventoryTransaction.Type.ToString(),
                        CustomerId = inventoryTransaction.clientId > 0 ? inventoryTransaction.clientId : (int?)null,
                        Note = inventoryTransaction.Note
                    },
                    ct);

                await _db.SaveChangesAsync(ct);

                if (transaction != null)
                {
                    await transaction.CommitAsync(ct);
                }

                return inventoryTransaction.Id;
            }
            catch (Exception)
            {
                if (transaction != null)
                {
                    await transaction.RollbackAsync(ct);
                }
                throw;
            }
            finally
            {
                if (transaction != null)
                {
                    await transaction.DisposeAsync();
                }
            }
        }

        public async Task<IReadOnlyList<InventoryTransactionResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.InventoryTransactions
                .AsNoTracking()
                .Include(t => t.Product)
                .Include(t => t.Customer)
                .OrderByDescending(t => t.TimestampUtc)
                .Take(take)
                .Select(t => new InventoryTransactionResponseDto
                {
                    Id = t.Id,
                    ProductId = t.ProductId,
                    ProductName = t.Product != null ? t.Product.Name : string.Empty,
                    CustomerId = t.clientId > 0 ? t.clientId : (int?)null,
                    CustomerName = t.Customer != null ? t.Customer.Name : null,
                    QuantityDelta = t.QuantityDelta,
                    Type = t.Type.ToString(),
                    TimestampUtc = t.TimestampUtc,
                    UserDisplayName = t.UserDisplayName,
                    ProductBatchId = t.ProductBatchId,
                    BatchNumber = t.BatchNumber,
                    Note = t.Note
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<InventoryTransactionResponseDto>> GetByProductAsync(int productId, CancellationToken ct = default)
        {
            if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId), "Product ID must be positive.");

            return await _db.InventoryTransactions
                .AsNoTracking()
                .Include(t => t.Product)
                .Include(t => t.Customer)
                .Where(t => t.ProductId == productId)
                .OrderByDescending(t => t.TimestampUtc)
                .Select(t => new InventoryTransactionResponseDto
                {
                    Id = t.Id,
                    ProductId = t.ProductId,
                    ProductName = t.Product != null ? t.Product.Name : string.Empty,
                    CustomerId = t.clientId > 0 ? t.clientId : (int?)null,
                    CustomerName = t.Customer != null ? t.Customer.Name : null,
                    QuantityDelta = t.QuantityDelta,
                    Type = t.Type.ToString(),
                    TimestampUtc = t.TimestampUtc,
                    UserDisplayName = t.UserDisplayName,
                    ProductBatchId = t.ProductBatchId,
                    BatchNumber = t.BatchNumber,
                    Note = t.Note
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<InventoryTransactionResponseDto>> GetTransactionsByCustomerAsync(int customerId, int take = 100, CancellationToken ct = default)
        {
            if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId), "Customer ID must be positive.");
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.InventoryTransactions
                .AsNoTracking()
                .Include(t => t.Product)
                .Include(t => t.Customer)
                .Where(t => t.clientId == customerId)
                .OrderByDescending(t => t.TimestampUtc)
                .Take(take)
                .Select(t => new InventoryTransactionResponseDto
                {
                    Id = t.Id,
                    ProductId = t.ProductId,
                    ProductName = t.Product != null ? t.Product.Name : string.Empty,
                    CustomerId = t.clientId > 0 ? t.clientId : (int?)null,
                    CustomerName = t.Customer != null ? t.Customer.Name : null,
                    QuantityDelta = t.QuantityDelta,
                    Type = t.Type.ToString(),
                    TimestampUtc = t.TimestampUtc,
                    UserDisplayName = t.UserDisplayName,
                    ProductBatchId = t.ProductBatchId,
                    BatchNumber = t.BatchNumber,
                    Note = t.Note
                })
                .ToListAsync(ct);
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
