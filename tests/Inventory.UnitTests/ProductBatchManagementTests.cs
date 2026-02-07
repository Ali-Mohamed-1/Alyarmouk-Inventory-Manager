using System;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Inventory.Application.DTOs.Product;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace Inventory.UnitTests
{
    public class ProductBatchManagementTests
    {
        private readonly AppDbContext _db;
        private readonly Mock<IAuditLogWriter> _auditWriterMock;
        private readonly Mock<IInventoryTransactionServices> _transactionServicesMock;
        private readonly ProductServices _productServices;

        public ProductBatchManagementTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new AppDbContext(options);
            _auditWriterMock = new Mock<IAuditLogWriter>();
            _transactionServicesMock = new Mock<IInventoryTransactionServices>();

            _productServices = new ProductServices(_db, _auditWriterMock.Object, _transactionServicesMock.Object);
        }

        [Fact]
        public async Task AddBatchAsync_ShouldCreateBatch_WhenValid()
        {
            // Arrange
            var category = new Inventory.Domain.Entities.Category { Id = 1, Name = "Test Category" };
            _db.categories.Add(category);
            
            var product = new Product { Name = "Test Product", Sku = "TEST-SKU", CategoryId = 1, IsActive = true };
            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            var req = new CreateBatchRequest
            {
                ProductId = product.Id,
                BatchNumber = "BATCH-001",
                UnitCost = 10,
                UnitPrice = 20,
                InitialQuantity = 0,
                Notes = "Test Note"
            };

            var user = new UserContext("user1", "User One");

            // Act
            var batchId = await _productServices.AddBatchAsync(req, user);

            // Assert
            var batch = await _db.ProductBatches.FindAsync(batchId);
            Assert.NotNull(batch);
            Assert.Equal("BATCH-001", batch.BatchNumber);
            Assert.Equal(10, batch.UnitCost);
            Assert.Equal(20, batch.UnitPrice);
            Assert.Equal(0, batch.OnHand);
        }

        [Fact]
        public async Task AddBatchAsync_ShouldCreateTransaction_WhenInitialQuantityProvided()
        {
            // Arrange
            var category = new Inventory.Domain.Entities.Category { Id = 1, Name = "Test Category" };
            _db.categories.Add(category);

            var product = new Product { Name = "Test Product 2", Sku = "TEST-SKU-2", CategoryId = 1, IsActive = true };
            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            var req = new CreateBatchRequest
            {
                ProductId = product.Id,
                BatchNumber = "BATCH-002",
                InitialQuantity = 100
            };

            var user = new UserContext("user1", "User One");

            // Act
            await _productServices.AddBatchAsync(req, user);

            // Assert
            _transactionServicesMock.Verify(s => s.CreateAsync(
                It.Is<CreateInventoryTransactionRequest>(r => 
                    r.ProductId == product.Id && 
                    r.Quantity == 100 && 
                    r.BatchNumber == "BATCH-002" &&
                    r.Type == InventoryTransactionType.Receive),
                user,
                It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task AddBatchAsync_ShouldThrow_WhenBatchExists()
        {
            // Arrange
            var category = new Inventory.Domain.Entities.Category { Id = 1, Name = "Test Category" };
            _db.categories.Add(category);

            var product = new Product { Name = "Test Product 3", Sku = "TEST-SKU-3", CategoryId = 1, IsActive = true };
            _db.Products.Add(product);
            await _db.SaveChangesAsync();

            _db.ProductBatches.Add(new ProductBatch { ProductId = product.Id, BatchNumber = "DUPLICATE-BATCH" });
            await _db.SaveChangesAsync();

            var req = new CreateBatchRequest
            {
                ProductId = product.Id,
                BatchNumber = "DUPLICATE-BATCH"
            };

            var user = new UserContext("user1", "User One");

            // Act & Assert
            await Assert.ThrowsAsync<ConflictException>(() => _productServices.AddBatchAsync(req, user));
        }
    }
}
