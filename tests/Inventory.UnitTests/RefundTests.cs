using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inventory.UnitTests
{
    public class RefundTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly SalesOrderServices _salesServices;
        private readonly PurchaseOrderServices _purchaseServices;
        private readonly CapturingInventoryServices _inventoryServices;
        private readonly CapturingFinancialServices _financialServices;
        private readonly UserContext _user;

        public RefundTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var auditMock = new MockAuditLogWriter();
            _inventoryServices = new CapturingInventoryServices();
            _financialServices = new CapturingFinancialServices();

            _salesServices = new SalesOrderServices(_db, auditMock, _inventoryServices, _financialServices);
            _purchaseServices = new PurchaseOrderServices(_db, auditMock, _inventoryServices, _financialServices);

            _user = new UserContext("test-user", "Test User");

            SeedData();
        }

        private void SeedData()
        {
            _db.Customers.Add(new Customer { Id = 1, Name = "Test Customer" });
            _db.Suppliers.Add(new Supplier { Id = 1, Name = "Test Supplier" });
            _db.Products.Add(new Product { Id = 1, Name = "Product A", Unit = "PCS", IsActive = true });
            _db.ProductBatches.Add(new ProductBatch { Id = 100, ProductId = 1, BatchNumber = "BATCH-001", UnitCost = 80, UnitPrice = 100, OnHand = 50 });
            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        [Fact]
        public async Task RefundSalesOrder_FullInternalRefund_UpdatesTotalsAndServices()
        {
            // Arrange: Create Paid & Done Order
            var orderId = await CreateAndCompleteSalesOrderAsync();

            var req = new RefundSalesOrderRequest
            {
                OrderId = orderId,
                Amount = 100 // Full refund of 1 item @ 100
                // No lines specified -> Money only? Or impl checks?
                // Logic: Logic allows Money Only.
            };

            // Act
            await _salesServices.RefundAsync(req, _user);

            // Assert
            var order = await _db.SalesOrders.FindAsync(orderId);
            Assert.Equal(100, order.RefundedAmount);
            
            // Verify Financial Service was called
            Assert.True(_financialServices.SalesRefundCalled);
            Assert.Equal(100, _financialServices.LastSalesRefundAmount);
        }

        [Fact]
        public async Task RefundSalesOrder_PartialProductRefund_UpdatesLineAndStock()
        {
            // Arrange
            var orderId = await CreateAndCompleteSalesOrderAsync();
            var lineId = _db.SalesOrderLines.First(l => l.SalesOrderId == orderId).Id;

            var req = new RefundSalesOrderRequest
            {
                OrderId = orderId,
                Amount = 50, // Partial Money
                LineItems = new List<RefundLineItem>
                {
                    new() { SalesOrderLineId = lineId, Quantity = 0.5m } // Return half item
                }
            };

            // Act
            await _salesServices.RefundAsync(req, _user);

            // Assert
            var order = await _db.SalesOrders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            var line = order.Lines.First();
            
            Assert.Equal(0.5m, line.RefundedQuantity);
            Assert.Equal(50, order.RefundedAmount);

            // Verify Stock Refund
            Assert.True(_inventoryServices.RefundSalesStockCalled);
            Assert.Single(_inventoryServices.LastRefundSalesLines);
            Assert.Equal(0.5m, _inventoryServices.LastRefundSalesLines[0].Quantity);
        }

        [Fact]
        public async Task RefundPurchaseOrder_ProductRefund_UpdatesLineAndStock()
        {
            // Arrange
            var orderId = await CreateAndReceivePurchaseOrderAsync();
            var lineId = _db.PurchaseOrderLines.First(l => l.PurchaseOrderId == orderId).Id;

            var req = new RefundPurchaseOrderRequest
            {
                OrderId = orderId,
                Amount = 100, // Full amount
                LineItems = new List<RefundPurchaseLineItem>
                {
                    new() { PurchaseOrderLineId = lineId, Quantity = 1 } 
                }
            };

            // Act
            await _purchaseServices.RefundAsync(req, _user);

            // Assert
            var order = await _db.PurchaseOrders.Include(o => o.Lines).FirstAsync(o => o.Id == orderId);
            var line = order.Lines.First();

            Assert.Equal(1m, line.RefundedQuantity);
            Assert.Equal(100, order.RefundedAmount);
            
             // Verify Stock Refund (Issue)
            Assert.True(_inventoryServices.RefundPurchaseStockCalled);
            Assert.Equal(1, _inventoryServices.LastRefundPurchaseLines[0].Quantity);
        }

        [Fact]
        public async Task RefundSalesOrder_OverRefund_ThrowsException()
        {
            var orderId = await CreateAndCompleteSalesOrderAsync();

            var req = new RefundSalesOrderRequest { OrderId = orderId, Amount = 101 }; // Total is 100

            await Assert.ThrowsAsync<ValidationException>(() => _salesServices.RefundAsync(req, _user));
        }

        [Fact]
        public async Task RefundSalesOrder_OverQuantity_ThrowsException()
        {
            var orderId = await CreateAndCompleteSalesOrderAsync();
            var lineId = _db.SalesOrderLines.First(l => l.SalesOrderId == orderId).Id;

            var req = new RefundSalesOrderRequest
            {
                OrderId = orderId,
                Amount = 100,
                LineItems = new List<RefundLineItem>
                {
                    new() { SalesOrderLineId = lineId, Quantity = 2 } // Only 1 sold
                }
            };

            await Assert.ThrowsAsync<ValidationException>(() => _salesServices.RefundAsync(req, _user));
        }

        private async Task<long> CreateAndCompleteSalesOrderAsync()
        {
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(1),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid,
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                }
            };

            var id = await _salesServices.CreateAsync(createReq, _user);
            await _salesServices.UpdateStatusAsync(id, new UpdateSalesOrderStatusRequest { Status = SalesOrderStatus.Done }, _user);
            return id;
        }

        private async Task<long> CreateAndReceivePurchaseOrderAsync()
        {
            var createReq = new CreatePurchaseOrderRequest
            {
                SupplierId = 1,
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreatePurchaseOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                }
            };

            var id = await _purchaseServices.CreateAsync(createReq, _user);
            // Receive
            await _purchaseServices.UpdateStatusAsync(id, PurchaseOrderStatus.Received, _user);
            // Pay
            await _purchaseServices.UpdatePaymentStatusAsync(id, PurchasePaymentStatus.Paid, _user);
            return id;
        }

        // --- Mocks ---

        public class CapturingInventoryServices : IInventoryServices
        {
        public bool RefundSalesStockCalled { get; private set; }
        public List<RefundLineItem> LastRefundSalesLines { get; private set; } = new();

        public bool RefundPurchaseStockCalled { get; private set; }
        public List<RefundPurchaseLineItem> LastRefundPurchaseLines { get; private set; } = new();

        public Task RefundSalesOrderStockAsync(long salesOrderId, List<RefundLineItem> lines, UserContext user, CancellationToken ct = default)
        {
            RefundSalesStockCalled = true;
            LastRefundSalesLines = lines;
            return Task.CompletedTask;
        }

        public Task RefundPurchaseOrderStockAsync(long purchaseOrderId, List<RefundPurchaseLineItem> lines, UserContext user, CancellationToken ct = default)
        {
            RefundPurchaseStockCalled = true;
            LastRefundPurchaseLines = lines;
            return Task.CompletedTask;
        }

        // --- Other Interface Methods (No-Op) ---
        public Task<decimal> GetOnHandAsync(int productId, CancellationToken ct = default) => Task.FromResult(100m);
        public Task<StockSnapshotResponseDto?> GetStockAsync(int productId, CancellationToken ct = default) => Task.FromResult<StockSnapshotResponseDto?>(null);
        public Task<IReadOnlyList<StockSnapshotResponseDto>> GetAllStockAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<StockSnapshotResponseDto>)new List<StockSnapshotResponseDto>());
        public Task ReceiveAsync(StockReceiveRequest req, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task IssueAsync(StockIssueRequest req, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task UpdateStockAsync(UpdateStockRequest req, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task<long> CreateTransactionAsync(CreateInventoryTransactionRequest req, UserContext user, CancellationToken ct = default) => Task.FromResult(0L);
        public Task<IReadOnlyList<InventoryTransactionResponseDto>> GetRecentTransactionsAsync(int take = 50, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<InventoryTransactionResponseDto>)new List<InventoryTransactionResponseDto>());
        public Task<IReadOnlyList<InventoryTransactionResponseDto>> GetProductTransactionsAsync(int productId, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<InventoryTransactionResponseDto>)new List<InventoryTransactionResponseDto>());
        public Task ProcessPurchaseOrderStockAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReversePurchaseOrderStockAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task ProcessSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReverseSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
    }

    public class CapturingFinancialServices : IFinancialServices
    {
        public bool SalesRefundCalled { get; private set; }
        public decimal LastSalesRefundAmount { get; private set; }
        
        public bool PurchaseRefundCalled { get; private set; }
        public decimal LastPurchaseRefundAmount { get; private set; }

        public Task ProcessSalesRefundPaymentAsync(long salesOrderId, decimal amount, UserContext user, CancellationToken ct = default)
        {
            SalesRefundCalled = true;
            LastSalesRefundAmount = amount;
            return Task.CompletedTask;
        }

        public Task ProcessPurchaseRefundPaymentAsync(long purchaseOrderId, decimal amount, UserContext user, CancellationToken ct = default)
        {
            PurchaseRefundCalled = true;
            LastPurchaseRefundAmount = amount;
            return Task.CompletedTask;
        }

        // --- Other Interface Methods (No-Op) ---
        public Task ProcessSalesPaymentAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task ProcessPurchasePaymentAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReverseSalesPaymentAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReversePurchasePaymentAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
    }

    public class MockAuditLogWriter : IAuditLogWriter
    {
        public Task LogCreateAsync<T>(object entityId, UserContext user, object? afterState = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
        public Task LogUpdateAsync<T>(object entityId, UserContext user, object? beforeState = null, object? afterState = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
        public Task LogDeleteAsync<T>(object entityId, UserContext user, object? beforeState = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
        public Task LogAsync(string entityType, string entityId, AuditAction action, UserContext user, string? changesJson = null, CancellationToken ct = default) => Task.CompletedTask;
    }
}
}
