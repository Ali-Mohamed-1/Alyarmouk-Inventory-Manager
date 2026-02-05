using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Inventory.UnitTests
{
    public class ProductBatchAvailabilityTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly InventoryServices _inventoryServices;
        private readonly SalesOrderServices _salesServices;
        private readonly UserContext _user;

        public ProductBatchAvailabilityTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var auditMock = new RefundTests.MockAuditLogWriter();
            var financialMock = new RefundTests.CapturingFinancialServices();
            var transactionServices = new InventoryTransactionServices(_db, auditMock);
            var stockSnapshotServices = new StockSnapshotServices(_db, auditMock);
            _inventoryServices = new InventoryServices(_db, transactionServices, stockSnapshotServices);
            _salesServices = new SalesOrderServices(_db, auditMock, _inventoryServices, financialMock);
            
            _user = new UserContext("test-user", "Test User");

            SeedData();
        }

        private void SeedData()
        {
            _db.Customers.Add(new Customer { Id = 1, Name = "Test Customer" });
            _db.Products.Add(new Product { Id = 1, Name = "Product A", Unit = "PCS", IsActive = true, ReorderPoint = 5 });
            
            // Initial unbatched stock
            _db.ProductBatches.Add(new ProductBatch { ProductId = 1, BatchNumber = "", OnHand = 100, Reserved = 0 });
            
            // Batch stock
            _db.ProductBatches.Add(new ProductBatch { ProductId = 1, BatchNumber = "BATCH-001", OnHand = 50, Reserved = 0 });
            
            _db.StockSnapshots.Add(new StockSnapshot { ProductId = 1 });
            
            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        [Fact]
        public async Task SalesOrder_Pending_IncreasesReserved()
        {
            // Arrange
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 10, BatchNumber = "BATCH-001" }
                }
            };

            // Act
            var orderId = await _salesServices.CreateAsync(createReq, _user);

            // Assert
            var batch = await _db.ProductBatches.FirstAsync(b => b.ProductId == 1 && b.BatchNumber == "BATCH-001");
            Assert.Equal(10, batch.Reserved);
            Assert.Equal(50, batch.OnHand);
            Assert.Equal(40, batch.Available);

            // Verify Product Level Summary
            var stock = await _inventoryServices.GetStockAsync(1);
            Assert.Equal(150, stock!.OnHand); // 100 unbatched + 50 batch
            Assert.Equal(10, stock.Reserved);
            Assert.Equal(140, stock.Available);
        }

        [Fact]
        public async Task SalesOrder_Done_DecreasesReservedAndOnHand()
        {
            // Arrange
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 10, BatchNumber = "BATCH-001" }
                }
            };
            var orderId = await _salesServices.CreateAsync(createReq, _user);

            // Act
            await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { Status = SalesOrderStatus.Done }, _user);

            // Assert
            var batch = await _db.ProductBatches.FirstAsync(b => b.ProductId == 1 && b.BatchNumber == "BATCH-001");
            Assert.Equal(0, batch.Reserved);
            Assert.Equal(40, batch.OnHand);
            Assert.Equal(40, batch.Available);

            var stock = await _inventoryServices.GetStockAsync(1);
            Assert.Equal(140, stock!.OnHand);
            Assert.Equal(0, stock.Reserved);
        }

        [Fact]
        public async Task SalesOrder_Cancelled_ReleasesReservation()
        {
            // Arrange
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 10, BatchNumber = "" } // Unbatched
                }
            };
            var orderId = await _salesServices.CreateAsync(createReq, _user);

            // Act
            await _salesServices.CancelAsync(orderId, _user);

            // Assert
            var batch = await _db.ProductBatches.FirstAsync(b => b.ProductId == 1 && b.BatchNumber == "");
            Assert.Equal(0, batch.Reserved);
            Assert.Equal(100, batch.OnHand);
        }

        [Fact]
        public async Task DoneOrder_MovingToPending_ReservesStockAgain()
        {
             // Arrange
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 10, BatchNumber = "BATCH-001" }
                }
            };
            var orderId = await _salesServices.CreateAsync(createReq, _user);
            await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { Status = SalesOrderStatus.Done }, _user);

            // Act
            await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { Status = SalesOrderStatus.Pending }, _user);

            // Assert
            var batch = await _db.ProductBatches.FirstAsync(b => b.ProductId == 1 && b.BatchNumber == "BATCH-001");
            Assert.Equal(10, batch.Reserved);
            Assert.Equal(50, batch.OnHand);
        }
    }
}
