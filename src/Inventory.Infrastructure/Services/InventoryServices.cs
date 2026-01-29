using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Application.DTOs.Transaction;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
                Note = req.Note
            };

            await _transactionServices.CreateAsync(transactionRequest, user, ct);
        }

        public async Task UpdateStockAsync(UpdateStockRequest req, UserContext user, CancellationToken ct = default)
        {
            await _snapshotServices.UpdateAsync(req, user, ct);
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
    }
}
