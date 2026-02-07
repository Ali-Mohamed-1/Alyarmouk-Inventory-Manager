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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class CrossViewConsistencyTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISalesOrderServices _salesServices;
    private readonly ICustomerServices _customerServices;
    private readonly INotificationService _notificationService;
    private readonly UserContext _user;

    public CrossViewConsistencyTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _salesServices = _scope.ServiceProvider.GetRequiredService<ISalesOrderServices>();
        _customerServices = _scope.ServiceProvider.GetRequiredService<ICustomerServices>();
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
    public async Task SameOrder_ConsistentData_AcrossDetailsCustomerHistoryNotifications()
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
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 3, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 150, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);

        var detailsDto = await _salesServices.GetByIdAsync(orderId, ct);
        Assert.NotNull(detailsDto);
        Assert.Equal(300, detailsDto.TotalAmount);
        Assert.Equal(150, detailsDto.PaidAmount);
        Assert.Equal(150, detailsDto.RemainingAmount);
        Assert.Equal(PaymentStatus.PartiallyPaid, detailsDto.PaymentStatus);
        Assert.Equal(SalesOrderStatus.Pending, detailsDto.Status);

        var customerOrders = await _salesServices.GetCustomerOrdersAsync(1, 100, ct);
        var fromHistory = customerOrders.FirstOrDefault(o => o.Id == orderId);
        Assert.NotNull(fromHistory);
        Assert.Equal(detailsDto.PaidAmount, fromHistory.PaidAmount);
        Assert.Equal(detailsDto.RemainingAmount, fromHistory.RemainingAmount);
        Assert.Equal(detailsDto.PaymentStatus, fromHistory.PaymentStatus);
        Assert.Equal(detailsDto.Status, fromHistory.Status);

        var notifications = (await _notificationService.GetActiveNotificationsAsync(ct)).ToList();
        var notif = notifications.FirstOrDefault(n => n.OrderId == orderId);
        Assert.NotNull(notif);
        Assert.Equal(150, notif.RemainingAmount);
    }
}
