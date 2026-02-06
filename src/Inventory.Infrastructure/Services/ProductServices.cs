using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Product;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services
{
    public sealed class ProductServices : IProductServices
    {
        private const int MaxSkuLength = 64;
        private const int MaxNameLength = 200;
        private const int MaxUnitLength = 32;
        private readonly AppDbContext _db;
        private readonly IAuditLogWriter _auditWriter;
        private readonly IInventoryTransactionServices _inventoryTransactions;

        public ProductServices(AppDbContext db, IAuditLogWriter auditWriter, IInventoryTransactionServices inventoryTransactions)
        {
            _db = db;
            _auditWriter = auditWriter;
            _inventoryTransactions = inventoryTransactions;
        }

        public async Task<IReadOnlyList<ProductResponseDto>> GetAllAsync(CancellationToken ct = default)
        {
            return await _db.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .OrderBy(p => p.Name)
                .Select(p => new ProductResponseDto
                {
                    Id = p.Id,
                    Sku = p.Sku,
                    Name = p.Name,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category != null ? p.Category.Name : string.Empty,
                    Unit = p.Unit,
                    ReorderPoint = p.ReorderPoint,
                    IsActive = p.IsActive,
                    RowVersion = Convert.ToBase64String(p.RowVersion)
                })
                .ToListAsync(ct);
        }

        public async Task<ProductResponseDto?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");

            return await _db.Products
                .AsNoTracking()
                .Include(p => p.Category)
                .Where(p => p.Id == id)
                .Select(p => new ProductResponseDto
                {
                    Id = p.Id,
                    Sku = p.Sku,
                    Name = p.Name,
                    CategoryId = p.CategoryId,
                    CategoryName = p.Category != null ? p.Category.Name : string.Empty,
                    Unit = p.Unit,
                    ReorderPoint = p.ReorderPoint,
                    IsActive = p.IsActive,
                    RowVersion = Convert.ToBase64String(p.RowVersion)
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task<int> CreateAsync(CreateProductRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            var normalizedSku = NormalizeAndValidateSku(req.Sku);
            var normalizedName = NormalizeAndValidateName(req.Name);
            var normalizedUnit = NormalizeUnit(req.Unit);

            if (req.CategoryId <= 0)
                throw new ValidationException("Category ID must be positive.");

            var categoryExists = await _db.categories
                .AsNoTracking()
                .AnyAsync(c => c.Id == req.CategoryId, ct);

            if (!categoryExists)
                throw new NotFoundException($"Category id {req.CategoryId} was not found.");

            var skuExists = await _db.Products
                .AsNoTracking()
                .AnyAsync(p => p.Sku == normalizedSku, ct);

            if (skuExists)
                throw new ConflictException($"Product SKU '{normalizedSku}' already exists.");

            var entity = new Inventory.Domain.Entities.Product
            {
                Sku = normalizedSku,
                Name = normalizedName,
                CategoryId = req.CategoryId,
                Unit = normalizedUnit,
                ReorderPoint = req.ReorderPoint,
                IsActive = req.IsActive
            };

            _db.Products.Add(entity);

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);

                var stockSnapshot = new Inventory.Domain.Entities.StockSnapshot
                {
                    ProductId = entity.Id,
                    OnHand = 0
                };
                _db.StockSnapshots.Add(stockSnapshot);

                await _db.SaveChangesAsync(ct);

                await _auditWriter.LogCreateAsync<Inventory.Domain.Entities.Product>(
                    entity.Id,
                    user,
                    afterState: new
                    {
                        Sku = entity.Sku,
                        Name = entity.Name,
                        CategoryId = entity.CategoryId,
                        Unit = entity.Unit,
                        ReorderPoint = entity.ReorderPoint,
                        IsActive = entity.IsActive
                    },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not create product due to a database conflict.", ex);
            }

            return entity.Id;
        }

        public async Task UpdateAsync(int id, UpdateProductRequest req, UserContext user, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.Id != 0 && req.Id != id)
                throw new ArgumentException("Route id does not match request id.", nameof(req));

            var normalizedSku = NormalizeAndValidateSku(req.Sku);
            var normalizedName = NormalizeAndValidateName(req.Name);
            var normalizedUnit = NormalizeUnit(req.Unit);

            if (req.CategoryId <= 0)
                throw new ValidationException("Category ID must be positive.");

            var categoryExists = await _db.categories
                .AsNoTracking()
                .AnyAsync(c => c.Id == req.CategoryId, ct);

            if (!categoryExists)
                throw new NotFoundException($"Category id {req.CategoryId} was not found.");

            byte[] expectedRowVersion;
            try
            {
                expectedRowVersion = Convert.FromBase64String(req.RowVersion);
            }
            catch (FormatException)
            {
                throw new ValidationException("Invalid RowVersion format.");
            }

            var entity = await _db.Products
                .SingleOrDefaultAsync(p => p.Id == id, ct);

            if (entity is null)
                throw new NotFoundException($"Product id {id} was not found.");

            var beforeState = new
            {
                Sku = entity.Sku,
                Name = entity.Name,
                CategoryId = entity.CategoryId,
                Unit = entity.Unit,
                ReorderPoint = entity.ReorderPoint,
                IsActive = entity.IsActive
            };

            var skuExists = await _db.Products
                .AsNoTracking()
                .AnyAsync(p => p.Id != id && p.Sku == normalizedSku, ct);

            if (skuExists)
                throw new ConflictException($"Product SKU '{normalizedSku}' already exists.");

            _db.Entry(entity).Property(p => p.RowVersion).OriginalValue = expectedRowVersion;

            entity.Sku = normalizedSku;
            entity.Name = normalizedName;
            entity.CategoryId = req.CategoryId;
            entity.Unit = normalizedUnit;
            entity.ReorderPoint = req.ReorderPoint;
            entity.IsActive = req.IsActive;

            var afterState = new
            {
                Sku = entity.Sku,
                Name = entity.Name,
                CategoryId = entity.CategoryId,
                Unit = entity.Unit,
                ReorderPoint = entity.ReorderPoint,
                IsActive = entity.IsActive
            };

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);

                await _auditWriter.LogUpdateAsync<Inventory.Domain.Entities.Product>(
                    entity.Id,
                    user,
                    beforeState: beforeState,
                    afterState: afterState,
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateConcurrencyException)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Product was modified by another user. Please refresh and try again.");
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update product due to a database conflict.", ex);
            }
        }

        public async Task SetActiveAsync(int id, bool isActive, UserContext user, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");
            ValidateUser(user);

            var entity = await _db.Products
                .SingleOrDefaultAsync(p => p.Id == id, ct);

            if (entity is null)
                throw new NotFoundException($"Product id {id} was not found.");

            // AUDIT LOG: Capture BEFORE state
            var beforeState = new { IsActive = entity.IsActive };

            entity.IsActive = isActive;

            // AUDIT LOG: Capture AFTER state
            var afterState = new { IsActive = entity.IsActive };

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                await _db.SaveChangesAsync(ct);

                // AUDIT LOG: Record the update
                await _auditWriter.LogUpdateAsync<Inventory.Domain.Entities.Product>(
                    entity.Id,
                    user,
                    beforeState: beforeState,
                    afterState: afterState,
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update product status due to a database conflict.", ex);
            }
        }

        public async Task<long> AddBatchAsync(CreateBatchRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.ProductId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req.ProductId), "Product ID must be positive.");

            var batchNumber = (req.BatchNumber ?? string.Empty).Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(batchNumber))
                throw new ValidationException("Batch Number is required.");

            var productExists = await _db.Products.AsNoTracking().AnyAsync(p => p.Id == req.ProductId, ct);
            if (!productExists)
                throw new NotFoundException($"Product id {req.ProductId} was not found.");

            var batchExists = await _db.ProductBatches
                .AsNoTracking()
                .AnyAsync(b => b.ProductId == req.ProductId && b.BatchNumber == batchNumber, ct);
            
            if (batchExists)
                throw new ConflictException($"Batch number '{batchNumber}' already exists for this product.");

            var batch = new Inventory.Domain.Entities.ProductBatch
            {
                ProductId = req.ProductId,
                BatchNumber = batchNumber,
                UnitCost = req.UnitCost,
                UnitPrice = req.UnitPrice,
                OnHand = 0, // Will be updated by transaction if needed
                Reserved = 0,
                Notes = req.Notes
            };

            _db.ProductBatches.Add(batch);

            // Use strategy to ensure transaction and logic are wrapped
            var strategy = _db.Database.CreateExecutionStrategy();
            
            return await strategy.ExecuteAsync(async () => 
            {
                await using var trans = await _db.Database.BeginTransactionAsync(ct);
                try
                {
                    await _db.SaveChangesAsync(ct); // Save to get Batch Id

                    // If initial quantity provided, create a transaction
                    if (req.InitialQuantity > 0)
                    {
                        var transactionReq = new CreateInventoryTransactionRequest
                        {
                            ProductId = req.ProductId,
                            Type = Inventory.Domain.Entities.InventoryTransactionType.Receive, // Initial stock is a Receive
                            Quantity = req.InitialQuantity,
                            ProductBatchId = batch.Id,
                            BatchNumber = batch.BatchNumber,
                            Note = "Initial Batch Stock"
                        };

                        // This service call handles updating OnHand and logging its own audit/transaction
                        // We pass the same user context.
                        // Ideally, we should enlist in the same transaction, but InventoryTransactionServices checks for existing transaction.
                        await _inventoryTransactions.CreateAsync(transactionReq, user, ct);
                    }

                    await _db.SaveChangesAsync(ct); // Ensure batch updates from transaction are saved if any
                    await trans.CommitAsync(ct);
                    return batch.Id;
                }
                catch (Exception)
                {
                    await trans.RollbackAsync(ct);
                    throw;
                }
            });
        }

        #region Helper Methods

        private static void ValidateUser(UserContext user)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.UserId)) throw new UnauthorizedAccessException("Missing user id.");
            if (string.IsNullOrWhiteSpace(user.UserDisplayName)) throw new UnauthorizedAccessException("Missing user display name.");
        }

        private static string NormalizeAndValidateSku(string? sku)
        {
            var normalized = (sku ?? string.Empty).Trim().ToUpperInvariant();

            if (normalized.Length == 0)
                throw new ValidationException("Product SKU is required.");

            if (normalized.Length > MaxSkuLength)
                throw new ValidationException($"Product SKU must be <= {MaxSkuLength} characters.");

            return normalized;
        }

        private static string NormalizeAndValidateName(string? name)
        {
            var normalized = (name ?? string.Empty).Trim();

            if (normalized.Length == 0)
                throw new ValidationException("Product name is required.");

            if (normalized.Length > MaxNameLength)
                throw new ValidationException($"Product name must be <= {MaxNameLength} characters.");

            return normalized;
        }

        private static string NormalizeUnit(string? unit)
        {
            var normalized = (unit ?? "pcs").Trim();

            if (normalized.Length == 0)
                return "pcs";

            if (normalized.Length > MaxUnitLength)
                throw new ValidationException($"Unit must be <= {MaxUnitLength} characters.");

            return normalized;
        }

        #endregion
    }
}
