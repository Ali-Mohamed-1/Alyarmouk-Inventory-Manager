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
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Inventory.UnitTests
{
    public class CancellationRulesTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly SalesOrderServices _salesServices;
        private readonly PurchaseOrderServices _purchaseServices;
        private readonly RefundTests.CapturingInventoryServices _inventory;
        private readonly RefundTests.CapturingFinancialServices _financial;
        private readonly UserContext _user;

        public CancellationRulesTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            _inventory = new RefundTests.CapturingInventoryServices();
            _financial = new RefundTests.CapturingFinancialServices();

            _salesServices = new SalesOrderServices(_db, _inventory, _financial);
            _purchaseServices = new PurchaseOrderServices(_db, _inventory, _financial);
            _user = new UserContext("test-user", "Test User");

            SeedData();
        }

        private void SeedData()
        {
            _db.Customers.Add(new Customer { Id = 1, Name = "Test Customer" });
            _db.Suppliers.Add(new Supplier { Id = 1, Name = "Test Supplier" });
            _db.Products.Add(new Product { Id = 1, Name = "Product A", Unit = "PCS", IsActive = true });
            _db.SaveChanges();
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }

        #region Sales Order Cancellation

        private async Task<long> CreateDoneSalesOrderAsync(decimal quantity, decimal unitPrice)
        {
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = quantity, UnitPrice = unitPrice }
                }
            };

            var id = await _salesServices.CreateAsync(createReq, _user);
            await _salesServices.UpdateStatusAsync(id, new UpdateSalesOrderStatusRequest { OrderId = id, Status = SalesOrderStatus.Done }, _user);
            return id;
        }

        [Fact]
        public async Task CannotCancelSalesOrder_WithRemainingStock()
        {
            var orderId = await CreateDoneSalesOrderAsync(2m, 50m);

            await Assert.ThrowsAsync<ValidationException>(() => _salesServices.CancelAsync(orderId, _user));
        }

        [Fact]
        public async Task CannotCancelSalesOrder_WithPaidAmount()
        {
            var orderId = await CreateDoneSalesOrderAsync(1m, 100m);

            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 50m,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);

            await Assert.ThrowsAsync<ValidationException>(() => _salesServices.CancelAsync(orderId, _user));
        }

        [Fact]
        public async Task CanCancelSalesOrder_WhenFullyRefundedStockAndMoney()
        {
            var orderId = await CreateDoneSalesOrderAsync(1m, 100m);

            await _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 100m,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);

            // Refund both Money AND Stock
            var order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId);
            var lineId = order!.Lines.First().Id;

            await _salesServices.RefundAsync(new RefundSalesOrderRequest
            {
                OrderId = orderId,
                Amount = 100m,
                LineItems = new List<RefundLineItem>
                {
                    // Full stock refund
                    new() { SalesOrderLineId = lineId, Quantity = 1m }
                }
            }, _user);

            await _salesServices.CancelAsync(orderId, _user);

            var updatedOrder = await _db.SalesOrders.FindAsync(orderId);
            Assert.NotNull(updatedOrder);
            Assert.Equal(SalesOrderStatus.Cancelled, updatedOrder!.Status);
        }

        [Fact]
        public async Task CancelledSalesOrder_CannotReceivePaymentsOrRefunds()
        {
            // Use a Pending order so we can cancel it immediately without needing refunds
            var createReq = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 1m, UnitPrice = 100m }
                }
            };
            var orderId = await _salesServices.CreateAsync(createReq, _user);

            await _salesServices.CancelAsync(orderId, _user);

            await Assert.ThrowsAsync<ValidationException>(() =>
                _salesServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
                {
                    Amount = 10m,
                    PaymentDate = DateTimeOffset.UtcNow,
                    PaymentMethod = PaymentMethod.Cash
                }, _user));

            await Assert.ThrowsAsync<ValidationException>(() =>
                _salesServices.RefundAsync(new RefundSalesOrderRequest
                {
                    OrderId = orderId,
                    Amount = 10m
                }, _user));
        }

        #endregion

        #region Purchase Order Cancellation

        private async Task<long> CreateReceivedPurchaseOrderAsync(decimal quantity, decimal unitPrice)
        {
            var createReq = new CreatePurchaseOrderRequest
            {
                SupplierId = 1,
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreatePurchaseOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = quantity, UnitPrice = unitPrice }
                }
            };

            var id = await _purchaseServices.CreateAsync(createReq, _user);
            await _purchaseServices.UpdateStatusAsync(id, PurchaseOrderStatus.Received, _user);
            return id;
        }

        [Fact]
        public async Task CannotCancelPurchaseOrder_WithRemainingStock()
        {
            var orderId = await CreateReceivedPurchaseOrderAsync(5m, 10m);

            await Assert.ThrowsAsync<ValidationException>(() => _purchaseServices.CancelAsync(orderId, _user));
        }

        [Fact]
        public async Task CannotCancelPurchaseOrder_WithPaidAmount()
        {
            var orderId = await CreateReceivedPurchaseOrderAsync(1m, 100m);

            await _purchaseServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 10m,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);

            await Assert.ThrowsAsync<ValidationException>(() => _purchaseServices.CancelAsync(orderId, _user));
        }

        [Fact]
        public async Task CanCancelPurchaseOrder_WhenFullyRefundedStockAndMoney()
        {
            var orderId = await CreateReceivedPurchaseOrderAsync(1m, 100m);

            await _purchaseServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
            {
                Amount = 100m,
                PaymentDate = DateTimeOffset.UtcNow,
                PaymentMethod = PaymentMethod.Cash
            }, _user);

            // Refund both Money AND Stock
            var order = await _db.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync(o => o.Id == orderId);
            var lineId = order!.Lines.First().Id;

            await _purchaseServices.RefundAsync(new RefundPurchaseOrderRequest
            {
                OrderId = orderId,
                Amount = 100m,
                LineItems = new List<RefundPurchaseLineItem>
                {
                    // Full stock refund
                    new() { PurchaseOrderLineId = lineId, Quantity = 1m }
                }
            }, _user);

            await _purchaseServices.CancelAsync(orderId, _user);

            var updatedOrder = await _db.PurchaseOrders.FindAsync(orderId);
            Assert.NotNull(updatedOrder);
            Assert.Equal(PurchaseOrderStatus.Cancelled, updatedOrder!.Status);
        }

        [Fact]
        public async Task CancelledPurchaseOrder_CannotReceivePaymentsOrRefunds()
        {
            // Use a Pending order so we can cancel it immediately without needing refunds
            var createReq = new CreatePurchaseOrderRequest
            {
                SupplierId = 1,
                IsTaxInclusive = false,
                ApplyVat = false,
                ApplyManufacturingTax = false,
                Lines = new List<CreatePurchaseOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 1m, UnitPrice = 100m }
                }
            };
            var orderId = await _purchaseServices.CreateAsync(createReq, _user);

            await _purchaseServices.CancelAsync(orderId, _user);

            await Assert.ThrowsAsync<ValidationException>(() =>
                _purchaseServices.AddPaymentAsync(orderId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
                {
                    Amount = 10m,
                    PaymentDate = DateTimeOffset.UtcNow,
                    PaymentMethod = PaymentMethod.Cash
                }, _user));

            await Assert.ThrowsAsync<ValidationException>(() =>
                _purchaseServices.RefundAsync(new RefundPurchaseOrderRequest
                {
                    OrderId = orderId,
                    Amount = 10m
                }, _user));
        }

        #endregion
    }
}
