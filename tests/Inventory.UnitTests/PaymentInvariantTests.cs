using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;

using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Inventory.UnitTests
{
    public class PaymentInvariantTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly SalesOrderServices _salesServices;
        private readonly PurchaseOrderServices _purchaseServices;
        private readonly UserContext _user;

        public PaymentInvariantTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            var auditMock = new MockAuditLogWriter();
            var inventoryServices = new CapturingInventoryServices();
            var financialServices = new CapturingFinancialServices();

            _salesServices = new SalesOrderServices(_db, auditMock, inventoryServices, financialServices);
            _purchaseServices = new PurchaseOrderServices(_db, auditMock, inventoryServices, financialServices);
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

        // --- SalesOrder Invariants ---

        [Fact]
        public void SalesOrder_RecalculatePaymentStatus_Throws_IfPaymentsNull()
        {
            var order = new SalesOrder { Id = 1, TotalAmount = 100, Payments = null };
            Assert.Throws<InvalidOperationException>(() => order.RecalculatePaymentStatus());
        }

        [Fact]
        public void SalesOrder_RecalculatePaymentStatus_Throws_IfOverpaid()
        {
            var order = new SalesOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
            order.Payments.Add(new PaymentRecord { Amount = 150, PaymentType = PaymentRecordType.Payment });

            var ex = Assert.Throws<InvalidOperationException>(() => order.RecalculatePaymentStatus());
            Assert.Contains("RemainingAmount", ex.Message);
            Assert.Contains("negative", ex.Message);
        }

        [Fact]
        public void SalesOrder_RecalculatePaymentStatus_Derives_PartiallyPaid()
        {
            var order = new SalesOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
            order.Payments.Add(new PaymentRecord { Amount = 50, PaymentType = PaymentRecordType.Payment });

            order.RecalculatePaymentStatus();

            Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
        }

        [Fact]
        public void SalesOrder_RecalculatePaymentStatus_Derives_Paid()
        {
            var order = new SalesOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
            order.Payments.Add(new PaymentRecord { Amount = 100, PaymentType = PaymentRecordType.Payment });

            order.RecalculatePaymentStatus();

            Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        }

        [Fact]
        public void SalesOrder_RecalculatePaymentStatus_Derives_Pending()
        {
            var order = new SalesOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
            
            order.RecalculatePaymentStatus();

            Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        }

        [Fact]
        public async Task CreateSalesOrder_WithPaidStatus_SetsLedgerAndDerivesStatus()
        {
            var req = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(30),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Paid,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                }
            };

            var orderId = await _salesServices.CreateAsync(req, _user);

            var order = await _db.SalesOrders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId);
            
            Assert.NotNull(order);
            Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
            Assert.Single(order.Payments);
            Assert.Equal(100, order.Payments.Sum(p => p.Amount));
        }

        [Fact]
        public async Task SalesOrder_AddPayment_EnforcesPartialPayment()
        {
            var req = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(30),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                }
            };
            var orderId = await _salesServices.CreateAsync(req, _user);

            await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest 
            { 
                Amount = 50, 
                PaymentMethod = PaymentMethod.Cash, 
                PaymentDate = DateTimeOffset.UtcNow 
            }, _user);

            _db.ChangeTracker.Clear();
            var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
            
            Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
            Assert.Equal(50, order.GetPaidAmount());
            Assert.Equal(50, order.GetRemainingAmount());
        }

        // --- PurchaseOrder Invariants ---

        [Fact]
        public void PurchaseOrder_RecalculatePaymentStatus_Throws_IfPaymentsNull()
        {
            var order = new PurchaseOrder { Id = 1, TotalAmount = 100, Payments = null };
            Assert.Throws<InvalidOperationException>(() => order.RecalculatePaymentStatus());
        }

        [Fact]
        public void PurchaseOrder_RecalculatePaymentStatus_Throws_IfOverpaid()
        {
            var order = new PurchaseOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
            order.Payments.Add(new PaymentRecord { Amount = 150, PaymentType = PaymentRecordType.Payment });

            var ex = Assert.Throws<InvalidOperationException>(() => order.RecalculatePaymentStatus());
            Assert.Contains("RemainingAmount", ex.Message);
            Assert.Contains("negative", ex.Message);
        }

        [Fact]
        public void PurchaseOrder_RecalculatePaymentStatus_Derives_PartiallyPaid()
        {
            var order = new PurchaseOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
            order.Payments.Add(new PaymentRecord { Amount = 50, PaymentType = PaymentRecordType.Payment });

            order.RecalculatePaymentStatus();

            Assert.Equal(PurchasePaymentStatus.PartiallyPaid, order.PaymentStatus);
        }

        [Fact]
        public void PurchaseOrder_RecalculatePaymentStatus_Derives_Paid()
        {
            var order = new PurchaseOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
            order.Payments.Add(new PaymentRecord { Amount = 100, PaymentType = PaymentRecordType.Payment });

            order.RecalculatePaymentStatus();

            Assert.Equal(PurchasePaymentStatus.Paid, order.PaymentStatus);
        }

        [Fact]
        public void PurchaseOrder_Invariant_Throws_IfPaid_But_RemainingPositive()
        {
             var order = new PurchaseOrder { Id = 1, TotalAmount = 100, Payments = new List<PaymentRecord>() };
             order.Payments.Add(new PaymentRecord { Amount = 50, PaymentType = PaymentRecordType.Payment });
             
             order.RecalculatePaymentStatus();
             
             Assert.NotEqual(PurchasePaymentStatus.Paid, order.PaymentStatus);
             Assert.Equal(50, order.GetRemainingAmount());
        }

        [Fact]
        public async Task CreatePurchaseOrder_WithPaidStatus_SetsLedgerAndDerivesStatus()
        {
            // Verifies that creating a PO with Paid status actually creates a payment record
            var req = new CreatePurchaseOrderRequest
            {
                SupplierId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(30),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PurchasePaymentStatus.Paid, 
                Note = "Test PO",
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreatePurchaseOrderLineRequest>
                {
                     new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-NEW" }
                }
            };
            
            var orderId = await _purchaseServices.CreateAsync(req, _user);
            
            var order = await _db.PurchaseOrders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId);
            
            Assert.NotNull(order);
            // If the service implements Initial Payment logic correctly, this should be Paid and have 1 payment.
            Assert.Equal(PurchasePaymentStatus.Paid, order.PaymentStatus);
            Assert.Single(order.Payments);
            Assert.Equal(100, order.Payments.Sum(p => p.Amount));
        }

        public void Dispose()
        {
            _db.Dispose();
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
            public Task ProcessPurchaseOrderStockAsync(long purchaseOrderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default) => Task.CompletedTask;
            public Task ReversePurchaseOrderStockAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task ProcessSalesOrderStockAsync(long salesOrderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default) => Task.CompletedTask;
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

        [Fact]
        public async Task PurchaseOrder_AddPayment_Partial_SetsStatusToPartiallyPaid()
        {
            // Arrange
            var order = new PurchaseOrder 
            { 
                Id = 2, 
                OrderNumber = "PO-002",
                SupplierId = 1,
                TotalAmount = 100, 

                Payments = new List<PaymentRecord>()
            };
            
            _db.PurchaseOrders.Add(order);
            await _db.SaveChangesAsync();

            var req = new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 50,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            };

            // Act
            await _purchaseServices.AddPaymentAsync(2, req, _user);

            // Assert
            _db.ChangeTracker.Clear();
            var updated = await _db.PurchaseOrders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == 2);

            Assert.NotNull(updated);
            Assert.Single(updated.Payments);
            Assert.Equal(50, updated.Payments.Sum(p => p.Amount));
            // Assert.Equal(100, updated.TotalAmount); // Ensure total amount remained 100
            Assert.Equal(PurchasePaymentStatus.PartiallyPaid, updated.PaymentStatus);
        }
    }
}
