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

public class PaymentMethodIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISalesOrderServices _salesServices;
    private readonly UserContext _user;

    public PaymentMethodIntegrationTests(IntegrationTestFixture fixture)
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
    public async Task PaymentMethod_Cash_CreatesLedgerCorrectly()
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
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 100,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);

        var payments = await _db.PaymentRecords.Where(p => p.SalesOrderId == orderId).ToListAsync(ct);
        Assert.Single(payments);
        Assert.Equal(PaymentRecordType.Payment, payments[0].PaymentType);
        Assert.Equal(PaymentMethod.Cash, payments[0].PaymentMethod);
    }

    [Fact]
    public async Task PaymentMethod_BankTransfer_WithTransferId_Persists()
    {
        var ct = CancellationToken.None;
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            PaymentMethod = PaymentMethod.BankTransfer,
            PaymentStatus = PaymentStatus.Paid,
            TransferId = "TR-12345",
            IsTaxInclusive = false,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        var order = await _db.SalesOrders.FirstAsync(o => o.Id == orderId, ct);
        Assert.Equal(PaymentMethod.BankTransfer, order.PaymentMethod);
        Assert.Equal("TR-12345", order.TransferId);
    }

    [Fact]
    public async Task Payment_Overpayment_Blocked()
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

        await Assert.ThrowsAsync<ValidationException>(() =>
            _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 150, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct));
    }

    [Fact]
    public async Task Refund_CreatesNegativeLedgerEntry()
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
        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = orderId, Amount = 50, Reason = "Partial refund" }, _user, ct);

        var payments = await _db.PaymentRecords.Where(p => p.SalesOrderId == orderId).ToListAsync(ct);
        Assert.Contains(payments, p => p.PaymentType == PaymentRecordType.Refund && p.Amount == 50);
    }
}
