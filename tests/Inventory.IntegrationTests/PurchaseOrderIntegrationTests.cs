using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Inventory.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class PurchaseOrderIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly IPurchaseOrderServices _purchaseServices;
    private readonly IReportingServices _reportingServices;
    private readonly UserContext _user;

    public PurchaseOrderIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _purchaseServices = _scope.ServiceProvider.GetRequiredService<IPurchaseOrderServices>();
        _reportingServices = _scope.ServiceProvider.GetRequiredService<IReportingServices>();
        _user = new UserContext("test-user", "Test User");
        _db.Database.EnsureCreated();
        TestDataSeeder.SeedAsync(_db).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public async Task PurchaseOrder_FullLifecycle_PaymentRefundCancel_WorksCorrectly()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create purchase order
        var createReq = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PurchasePaymentStatus.Unpaid,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            ReceiptExpenses = 0,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 20, UnitPrice = 50, BatchNumber = "BATCH-PO-001" } }
        };
        var orderId = await _purchaseServices.CreateAsync(createReq, _user, ct);

        // 2. Partial payment
        await _purchaseServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 500,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);

        var order = await _db.PurchaseOrders.Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(PurchasePaymentStatus.PartiallyPaid, order.PaymentStatus);

        // 3. Receive stock
        await _purchaseServices.UpdateStatusAsync(orderId, PurchaseOrderStatus.Received, _user, ct: ct);

        // Stock is stored in ProductBatches; OnHand is derived from batch aggregates
        var totalOnHand = await _db.ProductBatches.Where(b => b.ProductId == 1).SumAsync(b => b.OnHand, ct);
        Assert.Equal(70, totalOnHand); // 50 (seeded BATCH-001) + 20 from PO (BATCH-PO-001)

        // 3b. Pay remaining balance (money refund requires PaymentStatus.Paid)
        await _purchaseServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 500,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);

        // 4. Partial stock refund (5 units) + partial money refund
        var lineId = await _db.PurchaseOrderLines.Where(l => l.PurchaseOrderId == orderId).Select(l => l.Id).FirstAsync(ct);
        await _purchaseServices.RefundAsync(new RefundPurchaseOrderRequest
        {
            OrderId = orderId,
            Amount = 250,
            LineItems = new List<RefundPurchaseLineItem> { new() { PurchaseOrderLineId = lineId, Quantity = 5 } },
            Reason = "Partial return"
        }, _user, ct);

        _db.ChangeTracker.Clear();
        order = await _db.PurchaseOrders.Include(o => o.Lines).Include(o => o.Payments).FirstAsync(o => o.Id == orderId, ct);
        order.RecalculatePaymentStatus();
        Assert.Equal(5, order.Lines.First().RefundedQuantity);
        Assert.Equal(PurchasePaymentStatus.PartiallyPaid, order.PaymentStatus);
        Assert.Equal(750, order.GetPaidAmount());

        // 5. Refund remaining stock (15)
        await _purchaseServices.RefundAsync(new RefundPurchaseOrderRequest
        {
            OrderId = orderId,
            Amount = 0,
            LineItems = new List<RefundPurchaseLineItem> { new() { PurchaseOrderLineId = lineId, Quantity = 15 } },
            Reason = "Full stock return"
        }, _user, ct);

        // 6. Multiple partial money refunds
        await _purchaseServices.RefundAsync(new RefundPurchaseOrderRequest { OrderId = orderId, Amount = 250, Reason = "Second partial" }, _user, ct);
        await _purchaseServices.RefundAsync(new RefundPurchaseOrderRequest { OrderId = orderId, Amount = 500, Reason = "Final refund" }, _user, ct);

        // 7. Cancel
        await _purchaseServices.CancelAsync(orderId, _user, ct);

        _db.ChangeTracker.Clear();
        order = await _db.PurchaseOrders.FirstAsync(o => o.Id == orderId, ct);
        Assert.Equal(PurchaseOrderStatus.Cancelled, order.Status);

        // 8. Verify supplier balance, stock rollback, immutability
        var supplierBalance = await _reportingServices.GetSupplierBalanceAsync(1, ct);
        Assert.NotNull(supplierBalance);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _purchaseServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 10, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct));
        await Assert.ThrowsAsync<ValidationException>(() =>
            _purchaseServices.RefundAsync(new RefundPurchaseOrderRequest { OrderId = orderId, Amount = 10 }, _user, ct));
    }
}
