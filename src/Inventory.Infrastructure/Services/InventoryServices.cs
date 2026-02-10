using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Application.DTOs.Transaction;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Application.Exceptions;


namespace Inventory.Infrastructure.Services
{
    public sealed class InventoryServices : IInventoryServices
    {
        private const int MaxTake = 1000;
        private readonly AppDbContext _db;
        private readonly IInventoryTransactionServices _transactionServices;
        private readonly IStockSnapshotServices _snapshotServices;

        public InventoryServices(
            AppDbContext db,
            IInventoryTransactionServices transactionServices,
            IStockSnapshotServices snapshotServices)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _transactionServices = transactionServices ?? throw new ArgumentNullException(nameof(transactionServices));
            _snapshotServices = snapshotServices ?? throw new ArgumentNullException(nameof(snapshotServices));
        }

        public async Task<decimal> GetOnHandAsync(int productId, CancellationToken ct = default)
        {
            if (productId <= 0) throw new ArgumentOutOfRangeException(nameof(productId), "Product ID must be positive.");

            var snapshot = await _db.StockSnapshots
                .AsNoTracking()
                .Where(s => s.ProductId == productId)
                .Select(s => s.OnHand)
                .FirstOrDefaultAsync(ct);

            return snapshot; // Returns 0 if snapshot doesn't exist (default for decimal)
        }

        public async Task<StockSnapshotResponseDto?> GetStockAsync(int productId, CancellationToken ct = default)
        {
            return await _snapshotServices.GetByProductIdAsync(productId, ct);
        }

        public async Task<IReadOnlyList<StockSnapshotResponseDto>> GetAllStockAsync(CancellationToken ct = default)
        {
            return await _snapshotServices.GetAllAsync(ct);
        }

        public async Task ReceiveAsync(StockReceiveRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));

            if (req.ProductId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Product ID must be positive.");

            if (req.Quantity <= 0)
                throw new ValidationException("Quantity must be greater than zero.");

            // Create a Receive transaction
            var transactionRequest = new CreateInventoryTransactionRequest
            {
                ProductId = req.ProductId,
                Quantity = req.Quantity,
                Type = Domain.Entities.InventoryTransactionType.Receive,
                BatchNumber = req.BatchNumber,
                Note = req.Note
            };

            await _transactionServices.CreateAsync(transactionRequest, user, ct);
        }

        public async Task IssueAsync(StockIssueRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));

            if (req.ProductId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Product ID must be positive.");

            if (req.Quantity <= 0)
                throw new Exception("Quantity must be greater than zero.");

            // Create an Issue transaction
            var transactionRequest = new CreateInventoryTransactionRequest
            {
                ProductId = req.ProductId,
                Quantity = req.Quantity,
                Type = Domain.Entities.InventoryTransactionType.Issue,
                BatchNumber = req.BatchNumber,
                ProductBatchId = req.ProductBatchId,
                Note = req.Note
            };

            await _transactionServices.CreateAsync(transactionRequest, user, ct);
        }

        public async Task UpdateStockAsync(UpdateStockRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));

            var txReq = new CreateInventoryTransactionRequest
            {
                ProductId = req.ProductId,
                Quantity = req.Adjustment, // If negative, it reduces stock. If positive, it increases.
                Type = InventoryTransactionType.Adjust,
                BatchNumber = req.BatchNumber,
                ProductBatchId = req.ProductBatchId,
                Note = req.Note
            };

            await _transactionServices.CreateAsync(txReq, user, ct);
        }

        public async Task<long> CreateTransactionAsync(CreateInventoryTransactionRequest req, UserContext user, CancellationToken ct = default)
        {
            return await _transactionServices.CreateAsync(req, user, ct);
        }

        public async Task<IReadOnlyList<InventoryTransactionResponseDto>> GetRecentTransactionsAsync(int take = 50, CancellationToken ct = default)
        {
            return await _transactionServices.GetRecentAsync(take, ct);
        }

        public async Task<IReadOnlyList<InventoryTransactionResponseDto>> GetProductTransactionsAsync(int productId, CancellationToken ct = default)
        {
            return await _transactionServices.GetByProductAsync(productId, ct);
        }

        public async Task ProcessPurchaseOrderStockAsync(long purchaseOrderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct);
            if (order == null) throw new NotFoundException($"Purchase order {purchaseOrderId} not found.");

            foreach (var line in order.Lines)
            {
                // 1. Ensure Batch exists if batch number is provided
                if (!string.IsNullOrEmpty(line.BatchNumber))
                {
                    var batch = await _db.ProductBatches
                        .FirstOrDefaultAsync(b => b.ProductId == line.ProductId && b.BatchNumber == line.BatchNumber, ct);

                    if (batch == null)
                    {
                        var product = await _db.Products.FindAsync(new object[] { line.ProductId }, ct);
                        batch = new ProductBatch
                        {
                            ProductId = line.ProductId,
                            BatchNumber = line.BatchNumber,
                            UnitCost = line.UnitPrice,
                            UnitPrice = 0, // Default to 0 as Product.Price is removed
                            UpdatedUtc = DateTimeOffset.UtcNow
                        };
                        _db.ProductBatches.Add(batch);
                    }
                    else
                    {
                        batch.UnitCost = line.UnitPrice;
                        batch.UpdatedUtc = DateTimeOffset.UtcNow;
                    }
                }

                // 2. Weighted Average Cost calculation (REMOVED: Product.Cost is deprecated)
                // We now rely on specific batch costs.

                // 3. Create Inventory Transaction (Receive)
                var txReq = new CreateInventoryTransactionRequest
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    Type = InventoryTransactionType.Receive,
                    BatchNumber = line.BatchNumber,
                    CustomerId = null, // Not applicable for PO
                    Note = $"Purchase Order {order.OrderNumber}",
                    TimestampUtc = timestamp
                };

                await _transactionServices.CreateAsync(txReq, user, ct);
            }

            // Note: PurchaseOrderServices handles status update.
            await _db.SaveChangesAsync(ct);
        }

        public async Task ReversePurchaseOrderStockAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct);
            if (order == null) throw new NotFoundException($"Purchase order {purchaseOrderId} not found.");

            foreach (var line in order.Lines)
            {
                // Create Inventory Transaction (Issue) to reverse Receive
                var txReq = new CreateInventoryTransactionRequest
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    Type = InventoryTransactionType.Issue,
                    BatchNumber = line.BatchNumber,
                    Note = $"Reversal of Purchase Order {order.OrderNumber}"
                };

                await _transactionServices.CreateAsync(txReq, user, ct);
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task ReserveSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            foreach (var line in order.Lines)
            {
                var batchNumber = (line.BatchNumber ?? "").Trim();
                ProductBatch? batch = null;

                if (line.ProductBatchId.HasValue && line.ProductBatchId.Value > 0)
                {
                    batch = await _db.ProductBatches.FindAsync(new object[] { line.ProductBatchId.Value }, ct);
                }
                
                if (batch == null)
                {
                    batch = await _db.ProductBatches.FirstOrDefaultAsync(b => b.ProductId == line.ProductId && b.BatchNumber == batchNumber, ct);
                }

                if (batch == null)
                {
                    batch = new ProductBatch
                    {
                        ProductId = line.ProductId,
                        BatchNumber = batchNumber,
                        OnHand = 0,
                        Reserved = 0
                    };
                    _db.ProductBatches.Add(batch);
                    await _db.SaveChangesAsync(ct); // To get the ID
                }

                batch.Reserved += line.Quantity;
                line.ProductBatchId = batch.Id;
            }
            await _db.SaveChangesAsync(ct);
        }

        public async Task ReleaseSalesOrderReservationAsync(long salesOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            foreach (var line in order.Lines)
            {
                if (line.ProductBatchId.HasValue)
                {
                    var batch = await _db.ProductBatches.FindAsync(new object[] { line.ProductBatchId.Value }, ct);
                    if (batch != null)
                    {
                        batch.Reserved -= line.Quantity;
                        if (batch.Reserved < 0) batch.Reserved = 0;
                    }
                }
            }
            await _db.SaveChangesAsync(ct);
        }

        public async Task ProcessSalesOrderStockAsync(long salesOrderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            foreach (var line in order.Lines)
            {
                // Release reservation first
                if (line.ProductBatchId.HasValue)
                {
                    var batch = await _db.ProductBatches.FindAsync(new object[] { line.ProductBatchId.Value }, ct);
                    if (batch != null)
                    {
                        batch.Reserved -= line.Quantity;
                        if (batch.Reserved < 0) batch.Reserved = 0;
                    }
                }

                var txReq = new CreateInventoryTransactionRequest
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    Type = InventoryTransactionType.Issue,
                    BatchNumber = line.BatchNumber,
                    ProductBatchId = line.ProductBatchId,
                    CustomerId = order.CustomerId,
                    Note = $"Sales Order {order.OrderNumber}",
                    TimestampUtc = timestamp
                };

                await _transactionServices.CreateAsync(txReq, user, ct);
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task ReverseSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            foreach (var line in order.Lines)
            {
                var txReq = new CreateInventoryTransactionRequest
                {
                    ProductId = line.ProductId,
                    Quantity = line.Quantity,
                    Type = InventoryTransactionType.Receive, // Use Receive to put stock back
                    BatchNumber = line.BatchNumber,
                    ProductBatchId = line.ProductBatchId,
                    CustomerId = order.CustomerId,
                    Note = $"Reversal of Sales Order {order.OrderNumber}"
                };

                await _transactionServices.CreateAsync(txReq, user, ct);

                // Re-reserve the stock
                if (line.ProductBatchId.HasValue)
                {
                    var batch = await _db.ProductBatches.FindAsync(new object[] { line.ProductBatchId.Value }, ct);
                    if (batch != null)
                    {
                        batch.Reserved += line.Quantity;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
        }

        public async Task RefundSalesOrderStockAsync(long salesOrderId, List<RefundLineItem> lines, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.SalesOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == salesOrderId, ct);
            if (order == null) throw new NotFoundException($"Sales order {salesOrderId} not found.");

            foreach (var line in lines)
            {
                var txReq = new CreateInventoryTransactionRequest
                {
                    ProductId = await GetProductIdFromLineAsync(line.SalesOrderLineId, ct),
                    Quantity = line.Quantity,
                    Type = InventoryTransactionType.Receive, // Refund = Stock comes back
                    BatchNumber = line.BatchNumber,
                    ProductBatchId = line.ProductBatchId,
                    CustomerId = order.CustomerId,
                    Note = $"Refund for Sales Order {order.OrderNumber}"
                };

                await _transactionServices.CreateAsync(txReq, user, ct);
            }
        }

        public async Task RefundPurchaseOrderStockAsync(long purchaseOrderId, List<RefundPurchaseLineItem> lines, UserContext user, CancellationToken ct = default)
        {
             var order = await _db.PurchaseOrders
                .AsNoTracking()
                .FirstOrDefaultAsync(o => o.Id == purchaseOrderId, ct);
            if (order == null) throw new NotFoundException($"Purchase order {purchaseOrderId} not found.");

            foreach (var line in lines)
            {
                // We need the productId. Helper method or simple query.
                // Assuming we can get it from the line ID or trust the caller? 
                // Better to look it up to be safe and correct.
                var poLine = await _db.PurchaseOrderLines.FindAsync(new object[] { line.PurchaseOrderLineId }, ct);
                if (poLine == null) throw new NotFoundException($"Purchase Order Line {line.PurchaseOrderLineId} not found.");

                var txReq = new CreateInventoryTransactionRequest
                {
                    ProductId = poLine.ProductId,
                    Quantity = line.Quantity,
                    Type = InventoryTransactionType.Issue, // Refund = Stock goes back to supplier
                    BatchNumber = line.BatchNumber ?? poLine.BatchNumber,
                    Note = $"Refund for Purchase Order {order.OrderNumber}"
                };

                await _transactionServices.CreateAsync(txReq, user, ct);
            }
        }

        private async Task<int> GetProductIdFromLineAsync(long salesOrderLineId, CancellationToken ct)
        {
            var line = await _db.SalesOrderLines
                .AsNoTracking()
                .Select(l => new { l.Id, l.ProductId })
                .FirstOrDefaultAsync(l => l.Id == salesOrderLineId, ct);
            
            if (line == null) throw new NotFoundException($"Sales order line {salesOrderLineId} not found.");
            return line.ProductId;
        }
    }
}
