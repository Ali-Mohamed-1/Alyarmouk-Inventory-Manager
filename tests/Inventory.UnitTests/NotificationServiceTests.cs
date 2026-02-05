using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.DTOs;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Inventory.UnitTests
{
    public class NotificationServiceTests : IDisposable
    {
        private readonly AppDbContext _db;
        private readonly NotificationService _service;

        public NotificationServiceTests()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .ConfigureWarnings(x => x.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            _db = new AppDbContext(options);
            _db.Database.EnsureCreated();

            _service = new NotificationService(_db);

            SeedReferenceData();
        }

        private void SeedReferenceData()
        {
            _db.Customers.Add(new Customer { Id = 1, Name = "Customer A" });
            _db.Suppliers.Add(new Supplier { Id = 1, Name = "Supplier A" });
            _db.SaveChanges();
        }

        [Fact]
        public async Task SalesOrder_WithUpcomingDueDate_AndOutstandingBalance_IsReturned()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;

            var so = new SalesOrder
            {
                OrderNumber = "SO-NOTIF-001",
                CustomerId = 1,
                CustomerNameSnapshot = "Customer A",
                Status = SalesOrderStatus.Pending,
                OrderDate = now,
                DueDate = now.AddDays(3),
                PaymentMethod = PaymentMethod.Cash,
                CreatedUtc = now,
                CreatedByUserId = "test-user",
                CreatedByUserDisplayName = "Test User",
                TotalAmount = 100m
            };

            _db.SalesOrders.Add(so);
            await _db.SaveChangesAsync();

            // Act
            var notifications = (await _service.GetActiveNotificationsAsync(CancellationToken.None)).ToList();

            // Assert
            var notif = Assert.Single(notifications.Where(n => n.Type == "Sales"));
            Assert.Equal(so.Id, notif.OrderId);
            Assert.Equal(so.OrderNumber, notif.OrderNumber);
            Assert.Equal("Customer A", notif.CounterpartyName);
            Assert.Equal(100m, notif.RemainingAmount);
            Assert.InRange(notif.DaysUntilDue, 0, 7);
        }

        [Fact]
        public async Task SalesOrder_FullyPaid_IsNotReturned()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;

            var so = new SalesOrder
            {
                OrderNumber = "SO-NOTIF-PAID",
                CustomerId = 1,
                CustomerNameSnapshot = "Customer A",
                Status = SalesOrderStatus.Pending,
                OrderDate = now,
                DueDate = now.AddDays(5),
                PaymentMethod = PaymentMethod.Cash,
                CreatedUtc = now,
                CreatedByUserId = "test-user",
                CreatedByUserDisplayName = "Test User",
                TotalAmount = 100m
            };

            _db.SalesOrders.Add(so);
            await _db.SaveChangesAsync();

            _db.PaymentRecords.Add(new PaymentRecord
            {
                OrderType = OrderType.SalesOrder,
                SalesOrderId = so.Id,
                Amount = 100m,
                PaymentDate = now,
                PaymentMethod = PaymentMethod.Cash,
                PaymentType = PaymentRecordType.Payment
            });
            await _db.SaveChangesAsync();

            // Act
            var notifications = await _service.GetActiveNotificationsAsync(CancellationToken.None);

            // Assert
            Assert.DoesNotContain(notifications, n => n.Type == "Sales" && n.OrderId == so.Id);
        }

        [Fact]
        public async Task PurchaseOrder_WithUpcomingDeadline_AndOutstandingBalance_IsReturned()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;

            var po = new PurchaseOrder
            {
                OrderNumber = "PO-NOTIF-001",
                SupplierId = 1,
                SupplierNameSnapshot = "Supplier A",
                Status = PurchaseOrderStatus.Pending,
                PaymentMethod = PaymentMethod.Cash,
                CreatedUtc = now,
                CreatedByUserId = "test-user",
                CreatedByUserDisplayName = "Test User",
                PaymentDeadline = now.AddDays(4),
                TotalAmount = 250m
            };

            _db.PurchaseOrders.Add(po);
            await _db.SaveChangesAsync();

            // Act
            var notifications = (await _service.GetActiveNotificationsAsync(CancellationToken.None)).ToList();

            // Assert
            var notif = Assert.Single(notifications.Where(n => n.Type == "Purchase"));
            Assert.Equal(po.Id, notif.OrderId);
            Assert.Equal(po.OrderNumber, notif.OrderNumber);
            Assert.Equal("Supplier A", notif.CounterpartyName);
            Assert.Equal(250m, notif.RemainingAmount);
            Assert.InRange(notif.DaysUntilDue, 0, 7);
        }

        [Fact]
        public async Task PurchaseOrder_FullyPaid_IsNotReturned()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;

            var po = new PurchaseOrder
            {
                OrderNumber = "PO-NOTIF-PAID",
                SupplierId = 1,
                SupplierNameSnapshot = "Supplier A",
                Status = PurchaseOrderStatus.Pending,
                PaymentMethod = PaymentMethod.Cash,
                CreatedUtc = now,
                CreatedByUserId = "test-user",
                CreatedByUserDisplayName = "Test User",
                PaymentDeadline = now.AddDays(2),
                TotalAmount = 300m
            };

            _db.PurchaseOrders.Add(po);
            await _db.SaveChangesAsync();

            _db.PaymentRecords.Add(new PaymentRecord
            {
                OrderType = OrderType.PurchaseOrder,
                PurchaseOrderId = po.Id,
                Amount = 300m,
                PaymentDate = now,
                PaymentMethod = PaymentMethod.Cash,
                PaymentType = PaymentRecordType.Payment
            });
            await _db.SaveChangesAsync();

            // Act
            var notifications = await _service.GetActiveNotificationsAsync(CancellationToken.None);

            // Assert
            Assert.DoesNotContain(notifications, n => n.Type == "Purchase" && n.OrderId == po.Id);
        }

        [Fact]
        public async Task Orders_OutsideSevenDayWindow_AreNotReturned()
        {
            // Arrange
            var now = DateTimeOffset.UtcNow;

            var soFar = new SalesOrder
            {
                OrderNumber = "SO-FAR",
                CustomerId = 1,
                CustomerNameSnapshot = "Customer A",
                Status = SalesOrderStatus.Pending,
                OrderDate = now,
                DueDate = now.AddDays(10), // beyond 7-day window
                PaymentMethod = PaymentMethod.Cash,
                CreatedUtc = now,
                CreatedByUserId = "test-user",
                CreatedByUserDisplayName = "Test User",
                TotalAmount = 50m
            };

            var poFar = new PurchaseOrder
            {
                OrderNumber = "PO-FAR",
                SupplierId = 1,
                SupplierNameSnapshot = "Supplier A",
                Status = PurchaseOrderStatus.Pending,
                PaymentMethod = PaymentMethod.Cash,
                CreatedUtc = now,
                CreatedByUserId = "test-user",
                CreatedByUserDisplayName = "Test User",
                PaymentDeadline = now.AddDays(15),
                TotalAmount = 75m
            };

            _db.SalesOrders.Add(soFar);
            _db.PurchaseOrders.Add(poFar);
            await _db.SaveChangesAsync();

            // Act
            var notifications = await _service.GetActiveNotificationsAsync(CancellationToken.None);

            // Assert
            Assert.DoesNotContain(notifications, n => n.OrderNumber == "SO-FAR" || n.OrderNumber == "PO-FAR");
        }

        public void Dispose()
        {
            _db.Database.EnsureDeleted();
            _db.Dispose();
        }
    }
}

