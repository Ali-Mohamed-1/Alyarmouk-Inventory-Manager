using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class SalesOrderIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISalesOrderServices _salesServices;
    private readonly UserContext _user;

    public SalesOrderIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _salesServices = _scope.ServiceProvider.GetRequiredService<ISalesOrderServices>();
        _user = new UserContext("test-user", "Test User");
        _db.Database.EnsureCreated();
        TestDataSeeder.SeedAsync(_db).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public async Task SalesOrder_FullLifecycle_PaymentRefundCancel_WorksCorrectly()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create sales order (Pending, unpaid)
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
                new() { ProductId = 1, Quantity = 10, UnitPrice = 100, BatchNumber = "BATCH-001" }
            }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        // 2. Verify stock reserved, PaymentStatus = Pending
        var batch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-001", ct);
        Assert.Equal(10, batch.Reserved);
        var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(1000, order.TotalAmount);

        // 3. Record partial payment
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 500,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);

        // 4. Verify PartiallyPaid
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
        Assert.Equal(500, order.GetTotalPaid());
        Assert.Equal(500, order.GetPendingAmount());

        // 5. Mark order as Done
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);

        // 6. Verify stock issued (reservation released, OnHand decreased)
        _db.ChangeTracker.Clear();
        batch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-001", ct);
        Assert.Equal(40, batch.OnHand); // 50 - 10
        Assert.Equal(0, batch.Reserved);

        // 6b. Pay remaining balance (multiple partial payments → Paid)
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 500,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);

        // 7. Refund part of stock (5 units)
        var lineId = await _db.SalesOrderLines.Where(l => l.SalesOrderId == orderId).Select(l => l.Id).FirstAsync(ct);
        await _salesServices.RefundAsync(new RefundSalesOrderRequest
        {
            OrderId = orderId,
            Amount = 250,
            LineItems = new List<RefundLineItem> { new() { SalesOrderLineId = lineId, Quantity = 5 } },
            Reason = "Partial stock and money return"
        }, _user, ct);

        // 8. Verify PartiallyPaid after partial refund; RefundedQuantity tracked
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).Include(o => o.Lines).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(5, order.Lines.First().RefundedQuantity);
        Assert.Equal(250, order.RefundedAmount);
        Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
        Assert.Equal(750, order.GetTotalPaid());

        // 9. Refund remaining stock (5)
        await _salesServices.RefundAsync(new RefundSalesOrderRequest
        {
            OrderId = orderId,
            Amount = 0,
            LineItems = new List<RefundLineItem> { new() { SalesOrderLineId = lineId, Quantity = 5 } },
            Reason = "Full stock return"
        }, _user, ct);

        // 10. Multiple partial money refunds (PartiallyPaid must NOT block further refunds)
        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 250, Reason = "Second partial refund" }, _user, ct);
        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 500, Reason = "Final money refund" }, _user, ct);

        // 11. Cancel order
        await _salesServices.CancelAsync(orderId, _user, ct);

        // 12. Verify Status = Cancelled, no further mutations allowed
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.FirstAsync(o => o.Id == orderId, ct);
        Assert.Equal(SalesOrderStatus.Cancelled, order.Status);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 10, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 10 }, _user, ct));
    }

    [Fact]
    public async Task SalesOrder_CannotRefundMoney_WhenNetPaidIsZero()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 100 }, _user, ct));
        Assert.Contains("zero", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesOrder_CannotRefundStock_WhenStatusNotDone()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        var lineId = await _db.SalesOrderLines.Where(l => l.SalesOrderId == orderId).Select(l => l.Id).FirstAsync(ct);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.RefundAsync(new RefundSalesOrderRequest
            {
                OrderId = orderId,
                Amount = 0,
                LineItems = new List<RefundLineItem> { new() { SalesOrderLineId = lineId, Quantity = 1 } }
            }, _user, ct));
        Assert.Contains("stock", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesOrder_CannotCancel_WithNetPaidGreaterThanZero()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 100, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _salesServices.CancelAsync(orderId, _user, ct));
        Assert.Contains("refund", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesOrder_CannotCancel_WithRemainingStock()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _salesServices.CancelAsync(orderId, _user, ct));
        Assert.Contains("refund", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SalesOrder_CannotAddPayment_ToCancelledOrder()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.CancelAsync(orderId, _user, ct);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 10, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct));
    }

    [Fact]
    public async Task SalesOrder_CannotRefund_CancelledOrder()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.CancelAsync(orderId, _user, ct);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 10 }, _user, ct));
    }

    [Fact]
    public async Task SalesOrder_MultiplePartialPayments_ReachesPaid()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 200, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 400, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);
        _db.ChangeTracker.Clear();
        var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
        Assert.Equal(400, order.GetTotalPaid());

        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 600, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(1000, order.GetTotalPaid());
    }

    [Fact]
    public async Task SalesOrder_MultiplePartialMoneyRefunds_UntilNetPaidZero()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 200, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 1000, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 300, Reason = "First partial refund" }, _user, ct);
        _db.ChangeTracker.Clear();
        var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
        Assert.Equal(700, order.GetTotalPaid());

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 400, Reason = "Second partial refund" }, _user, ct);
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);
        Assert.Equal(300, order.GetTotalPaid());

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 300, Reason = "Final refund" }, _user, ct);
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.Pending, order.PaymentStatus);
        Assert.Equal(0, order.GetTotalPaid());
    }

    [Fact]
    public async Task SalesOrder_Interleaved_PayRefundPayRefund_WorksCorrectly()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 3, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);

        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 200, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);
        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 100, Reason = "Refund some" }, _user, ct);
        _db.ChangeTracker.Clear();
        var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(100, order.GetTotalPaid());

        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 200, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(300, order.GetTotalPaid());

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 200, Reason = "Second partial refund" }, _user, ct);
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(100, order.GetTotalPaid());

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 100, Reason = "Final refund" }, _user, ct);
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(0, order.GetTotalPaid());
    }

    [Fact]
    public async Task SalesOrder_PartiallyPaid_DoesNotBlockFurtherRefunds()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 2, UnitPrice = 500, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 1000, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 600, Reason = "First refund → PartiallyPaid" }, _user, ct);
        _db.ChangeTracker.Clear();
        var order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PaymentStatus.PartiallyPaid, order.PaymentStatus);

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 400, Reason = "Second refund while PartiallyPaid" }, _user, ct);
        _db.ChangeTracker.Clear();
        order = await _db.SalesOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(0, order.GetTotalPaid());
    }

    [Fact]
    public async Task SalesOrder_CannotUpdateStatus_ToCancelledViaUpdateStatus()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Cancelled }, _user, ct: ct));
        Assert.Contains("cancel", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
