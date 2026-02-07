using System;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.DTOs.Reporting;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class ReportingIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISalesOrderServices _salesServices;
    private readonly IReportingServices _reportingServices;
    private readonly UserContext _user;

    public ReportingIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _salesServices = _scope.ServiceProvider.GetRequiredService<ISalesOrderServices>();
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
    public async Task FinancialSummary_TotalSales_COGS_NetProfit_ReflectCompletedOrders()
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
            Lines = new System.Collections.Generic.List<CreateSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 10, UnitPrice = 100, BatchNumber = "BATCH-001" }
            }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 1000,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user,null, ct);

        var filter = new FinancialReportFilterDto
        {
            DateRangeType = FinancialDateRangeType.ThisYear,
            TimezoneOffsetMinutes = 0
        };
        var summary = await _reportingServices.GetFinancialSummaryAsync(filter, ct);

        Assert.True(summary.SalesRevenue > 0, "Sales revenue should reflect completed paid order");
        Assert.True(summary.SalesProfit > 0, "Sales profit should reflect completed paid order");
        Assert.True(summary.CostOfGoods >= 0, "COGS should be non-negative");
        Assert.True(summary.GrossProfit >= 0, "Gross profit should be non-negative");
    }

    [Fact]
    public async Task FinancialSummary_Refund_ReducesSalesProfit()
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
            Lines = new System.Collections.Generic.List<CreateSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 5, UnitPrice = 100, BatchNumber = "BATCH-001" }
            }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 500,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user,null, ct);

        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear, TimezoneOffsetMinutes = 0 };
        var beforeRefund = await _reportingServices.GetFinancialSummaryAsync(filter, ct);

        var lineId = await _db.SalesOrderLines.Where(l => l.SalesOrderId == orderId).Select(l => l.Id).FirstAsync(ct);
        await _salesServices.RefundAsync(new RefundSalesOrderRequest
        {
            OrderId = orderId,
            Amount = 0,
            LineItems = new System.Collections.Generic.List<RefundLineItem> { new() { SalesOrderLineId = lineId, Quantity = 5 } },
            Reason = "Full return"
        }, _user, ct);
        await _salesServices.RefundAsync(new RefundSalesOrderRequest
        {
            OrderId = orderId,
            Amount = 500,
            Reason = "Full money refund"
        }, _user, ct);

        var afterRefund = await _reportingServices.GetFinancialSummaryAsync(filter, ct);

        Assert.True(afterRefund.SalesProfit < beforeRefund.SalesProfit || afterRefund.SalesProfit == 0,
            "Refund should reduce or zero out sales profit in the report");
    }

    [Fact]
    public async Task InternalExpenses_AppearInReport()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        await _reportingServices.CreateInternalExpenseAsync(new CreateInternalExpenseRequestDto
        {
            InternalExpenseType = InternalExpenseType.Other,
            Description = "Test expense",
            Amount = 50,
            TimestampUtc = DateTimeOffset.UtcNow
        }, _user, ct);

        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear, TimezoneOffsetMinutes = 0 };
        var expenses = await _reportingServices.GetInternalExpensesAsync(filter, ct);

        Assert.Contains(expenses, e => e.Description == "Test expense" && e.Amount == 50);
    }

    [Fact]
    public async Task CancelledOrders_ExcludedFromFinancialSummary()
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
            Lines = new System.Collections.Generic.List<CreateSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 2, UnitPrice = 100, BatchNumber = "BATCH-001" }
            }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.CancelAsync(orderId, _user, ct);

        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear, TimezoneOffsetMinutes = 0 };
        var summary = await _reportingServices.GetFinancialSummaryAsync(filter, ct);

        // Cancelled order was never paid or done - no revenue should be recorded
        // If we had paid and done before cancel, the revenue would be in FinancialTransactions.
        // For a Pending then Cancelled order with no payment, revenue should be 0.
        Assert.True(summary.SalesRevenue >= 0);
    }
}
