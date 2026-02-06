using System.Collections.Generic;
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

public class NegativeAndRegressionTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISalesOrderServices _salesServices;
    private readonly INotificationService _notificationService;
    private readonly UserContext _user;

    public NegativeAndRegressionTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _salesServices = _scope.ServiceProvider.GetRequiredService<ISalesOrderServices>();
        _notificationService = _scope.ServiceProvider.GetRequiredService<INotificationService>();
        _user = new UserContext("test-user", "Test User");
        _db.Database.EnsureCreated();
        TestDataSeeder.SeedAsync(_db).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public async Task DoubleCancel_Blocked()
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

        await Assert.ThrowsAsync<ValidationException>(() => _salesServices.CancelAsync(orderId, _user, ct));
    }

    [Fact]
    public async Task DoubleRefund_BlockedWhenFullyRefunded()
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
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);
        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 100, Reason = "Full refund" }, _user, ct);

        await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 1, Reason = "Double refund attempt" }, _user, ct));
    }

    [Fact]
    public async Task Notifications_NeverIncludePaidOrders()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(1),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        var notifications = await _notificationService.GetActiveNotificationsAsync(ct);
        Assert.DoesNotContain(notifications, n => n.OrderId == orderId);
    }

    [Fact]
    public async Task Notifications_NeverIncludeCancelledOrders()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(1),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.CancelAsync(orderId, _user, ct);

        var notifications = await _notificationService.GetActiveNotificationsAsync(ct);
        Assert.DoesNotContain(notifications, n => n.OrderId == orderId);
    }

    [Fact]
    public async Task Notifications_NeverIncludeRemainingAmountZero()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(1),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 100, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);

        var notifications = await _notificationService.GetActiveNotificationsAsync(ct);
        Assert.DoesNotContain(notifications, n => n.OrderId == orderId);
    }
}
