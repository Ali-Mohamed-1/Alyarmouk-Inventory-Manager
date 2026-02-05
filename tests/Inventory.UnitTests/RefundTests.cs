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
            };

            // Act
            await _salesServices.RefundAsync(req, _user);

            // Assert
            var order = await _db.SalesOrders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId);
            Assert.Equal(100, order.RefundedAmount);
            Assert.Contains(order.Payments, p => p.PaymentType == PaymentRecordType.Refund && p.Amount == 100);
            
            // Verify Financial Service was called
            Assert.True(_financialServices.CreationCalled);
            Assert.Contains(_financialServices.CreatedPayments, p => p.PaymentType == PaymentRecordType.Refund && p.Amount == 100);
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
            var order = await _db.SalesOrders.Include(o => o.Lines).Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            var line = order.Lines.First();
            
            Assert.Equal(0.5m, line.RefundedQuantity);
            Assert.Equal(50, order.RefundedAmount);
            Assert.Contains(order.Payments, p => p.PaymentType == PaymentRecordType.Refund && p.Amount == 50);

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
            var order = await _db.PurchaseOrders.Include(o => o.Lines).Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            var line = order.Lines.First();

            Assert.Equal(1m, line.RefundedQuantity);
            Assert.Equal(100, order.RefundedAmount);
            Assert.Contains(order.Payments, p => p.PaymentType == PaymentRecordType.Refund && p.Amount == 100);
            
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

        // =============================================
        // PARTIAL PAYMENT TESTS
        // =============================================

        [Fact]
        public async Task AddPayment_PartialPayment_SetsStatusToPartiallyPaid()
        {
            // Arrange: Create order with total $1000
            var orderId = await CreateSalesOrderWithTotalAsync(1000m);

            // Act: Pay 30% ($300)
            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 300,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);

            // Assert
            _db.ChangeTracker.Clear();
            var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
            Assert.Equal(300, order.GetPaidAmount());
            Assert.Equal(700, order.GetRemainingAmount());
        }

        [Fact]
        public async Task AddPayment_MultiplePartialPayments_SumsCorrectly()
        {
            // Arrange
            var orderId = await CreateSalesOrderWithTotalAsync(1000m);
            _db.ChangeTracker.Clear(); // Clear tracking to ensure fresh loads

            // Act: Pay $300, then $200, then $500
            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 300,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);
            _db.ChangeTracker.Clear();

            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 200,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);
            _db.ChangeTracker.Clear();

            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 500,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);

            // Assert
            _db.ChangeTracker.Clear();
            var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
            Assert.Equal(1000, order.GetPaidAmount());
            Assert.Equal(0, order.GetRemainingAmount());
            Assert.Equal(3, order.Payments.Count(p => p.PaymentType == PaymentRecordType.Payment));
        }

        [Fact]
        public async Task AddPayment_FullPaymentAfterPartial_SetsStatusToPaid()
        {
            // Arrange
            var orderId = await CreateSalesOrderWithTotalAsync(1000m);
            _db.ChangeTracker.Clear();

            // Act: Pay 30% then remaining 70%
            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 300,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);
            _db.ChangeTracker.Clear();

            // Verify it's partially paid first
            var orderPartial = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            Assert.Equal(PaymentStatus.PartiallyPaid, orderPartial.PaymentStatus);
            _db.ChangeTracker.Clear();

            // Pay remaining
            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 700,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);
            _db.ChangeTracker.Clear();

            // Assert full payment
            var orderFull = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            Assert.Equal(PaymentStatus.Paid, orderFull.PaymentStatus);
            Assert.Equal(1000, orderFull.GetPaidAmount());
            Assert.Equal(0, orderFull.GetRemainingAmount());
        }

        [Fact]
        public async Task GetByIdAsync_ReturnsCorrectDtoAfterPartialPayment()
        {
            // Arrange
            var orderId = await CreateSalesOrderWithTotalAsync(1000m);

            // Pay $400
            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 400,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);

            // Act: Get DTO
            var dto = await _salesServices.GetByIdAsync(orderId);

            // Assert: DTO fields are derived correctly
            Assert.NotNull(dto);
            Assert.Equal(1000, dto.TotalAmount);
            Assert.Equal(400, dto.PaidAmount);
            Assert.Equal(600, dto.RemainingAmount);
            Assert.Equal(PaymentStatus.PartiallyPaid, dto.PaymentStatus);
        }

        [Fact]
        public async Task Refund_AfterFullPayment_DowngradesStatus()
        {
            // Arrange: Create paid order
            var orderId = await CreateAndCompleteSalesOrderAsync();
            _db.ChangeTracker.Clear();
            
            // Act: Partial refund
            await _salesServices.RefundAsync(new RefundSalesOrderRequest
            {
                OrderId = orderId,
                Amount = 30  // Refund $30 of $100 total
            }, _user);
            _db.ChangeTracker.Clear();

            // Assert: Status should downgrade
            var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
            Assert.Equal(70, order.GetPaidAmount());  // 100 paid - 30 refunded
            Assert.Equal(30, order.GetRemainingAmount());
        }

        private async Task<long> CreateSalesOrderWithTotalAsync(decimal total)
        {
            // Calculate unit price to get desired total (1 item)
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(30),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 10, UnitPrice = total / 10 }  // 10 items @ total/10 = total
                }
            };

            return await _salesServices.CreateAsync(createReq, _user);
        }

        private async Task<long> CreateAndCompleteSalesOrderAsync()
        {
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(1),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid, // CreateAsync refactored to handle this via ledger
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
            // Pay via AddPayment
            await _purchaseServices.AddPaymentAsync(id, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 100,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash,
                Reference = "INIT-PAY"
            }, _user);
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
        public Task ReserveSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        public Task ReleaseSalesOrderReservationAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
    }

    public class CapturingFinancialServices : IFinancialServices
    {
        public bool CreationCalled { get; private set; }
        public List<PaymentRecord> CreatedPayments { get; private set; } = new();

        public Task CreateFinancialTransactionFromPaymentAsync(PaymentRecord payment, UserContext user, CancellationToken ct = default)
        {
            CreationCalled = true;
            CreatedPayments.Add(payment);
            return Task.CompletedTask;
        }
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
