using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class NotificationIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISalesOrderServices _salesServices;
    private readonly INotificationService _notificationService;
    private readonly UserContext _user;

    public NotificationIntegrationTests(IntegrationTestFixture fixture)
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
    public async Task Notifications_Upcoming_Unpaid_Appears()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(3),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        var notifications = (await _notificationService.GetActiveNotificationsAsync(ct)).ToList();
        var notif = notifications.FirstOrDefault(n => n.OrderId == orderId && n.Type == "Sales");
        Assert.NotNull(notif);
        Assert.Equal(100m, notif.RemainingAmount);
    }

    [Fact]
    public async Task Notifications_Overdue_Unpaid_Appears()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(-2),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        var notifications = (await _notificationService.GetActiveNotificationsAsync(ct)).ToList();
        var notif = notifications.FirstOrDefault(n => n.OrderId == orderId && n.Type == "Sales");
        Assert.NotNull(notif);
        Assert.True(notif.DaysUntilDue < 0);
    }

    [Fact]
    public async Task Notifications_Paid_Disappears()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(5),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Paid,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        var notifications = (await _notificationService.GetActiveNotificationsAsync(ct)).ToList();
        Assert.DoesNotContain(notifications, n => n.OrderId == orderId && n.Type == "Sales");
    }

    [Fact]
    public async Task Notifications_Cancelled_Disappears()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(5),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.CancelAsync(orderId, _user, ct);

        var notifications = (await _notificationService.GetActiveNotificationsAsync(ct)).ToList();
        Assert.DoesNotContain(notifications, n => n.OrderId == orderId && n.Type == "Sales");
    }

    [Fact]
    public async Task Notifications_RemainingAmountZero_Disappears()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(5),
            PaymentMethod = PaymentMethod.Cash,
            PaymentStatus = PaymentStatus.Pending,
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 100, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);

        var notifications = (await _notificationService.GetActiveNotificationsAsync(ct)).ToList();
        Assert.DoesNotContain(notifications, n => n.OrderId == orderId && n.Type == "Sales");
    }
}
