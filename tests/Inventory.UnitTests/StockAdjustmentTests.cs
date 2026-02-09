using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.UnitTests
{
    public class StockAdjustmentTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly InventoryServices _inventoryServices;
        private readonly InventoryTransactionServices _transactionServices;
        private readonly UserContext _user;

        public StockAdjustmentTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            _transactionServices = new InventoryTransactionServices(_db);
            // Mocking snapshot services for simplest setup
            var snapshotMock = new Moq.Mock<IStockSnapshotServices>();
            
            _inventoryServices = new InventoryServices(_db, _transactionServices, snapshotMock.Object);
            _user = new UserContext("test-user", "Test User");

            SeedData();
        }

        private void SeedData()
        {
            _db.Products.Add(new Product { Id = 1, Name = "Product A", Unit = "PCS", IsActive = true });
            _db.ProductBatches.Add(new ProductBatch 
            { 
                Id = 100, 
                ProductId = 1, 
                BatchNumber = "BATCH-001", 
                UnitCost = 80, 
                UnitPrice = 100, 
                OnHand = 100 // Starting with 100
            });
            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        [Fact]
        public async Task UpdateStockAsync_WhenAdjustingUp_CalculatesPositiveDelta()
        {
            // Arrange
            var req = new UpdateStockRequest
            {
                ProductId = 1,
                ProductBatchId = 100,
                BatchNumber = "BATCH-001",
                NewQuantity = 600, // 100 -> 600 means +500 adjustment
                Adjustment = 0,
                Note = "Adjustment test up",
                RowVersion = "First"
            };

            // Act
            await _inventoryServices.UpdateStockAsync(req, _user);

            // Assert
            var batch = await _db.ProductBatches.FindAsync(100L);
            Assert.Equal(600, batch.OnHand);

            var transaction = await _db.InventoryTransactions.OrderByDescending(t => t.Id).FirstOrDefaultAsync();
            Assert.NotNull(transaction);
            Assert.Equal(500, transaction.QuantityDelta);
            Assert.Equal(InventoryTransactionType.Adjust, transaction.Type);
        }

        [Fact]
        public async Task UpdateStockAsync_WhenAdjustingDown_CalculatesNegativeDelta()
        {
            // Arrange
            var req = new UpdateStockRequest
            {
                ProductId = 1,
                ProductBatchId = 100,
                BatchNumber = "BATCH-001",
                NewQuantity = 40, // 100 -> 40 means -60 adjustment
                Adjustment = 0,
                Note = "Adjustment test down",
                RowVersion = "First"
            };

            // Act
            await _inventoryServices.UpdateStockAsync(req, _user);

            // Assert
            var batch = await _db.ProductBatches.FindAsync(100L);
            Assert.Equal(40, batch.OnHand);

            var transaction = await _db.InventoryTransactions.OrderByDescending(t => t.Id).FirstOrDefaultAsync();
            Assert.NotNull(transaction);
            Assert.Equal(-60, transaction.QuantityDelta);
            Assert.Equal(InventoryTransactionType.Adjust, transaction.Type);
        }

        [Fact]
        public async Task UpdateStockAsync_WhenNoChange_CalculatesZeroDeltaAndSucceeds()
        {
            // Arrange
            var req = new UpdateStockRequest
            {
                ProductId = 1,
                ProductBatchId = 100,
                BatchNumber = "BATCH-001",
                NewQuantity = 100, // 100 -> 100 means 0 adjustment
                Adjustment = 0,
                Note = "Adjustment test zero",
                RowVersion = "First"
            };

            // Act
            await _inventoryServices.UpdateStockAsync(req, _user);

            // Assert
            var batch = await _db.ProductBatches.FindAsync(100L);
            Assert.Equal(100, batch.OnHand);

            var transaction = await _db.InventoryTransactions.OrderByDescending(t => t.Id).FirstOrDefaultAsync();
            Assert.NotNull(transaction);
            Assert.Equal(0, transaction.QuantityDelta);
        }
    }
}
