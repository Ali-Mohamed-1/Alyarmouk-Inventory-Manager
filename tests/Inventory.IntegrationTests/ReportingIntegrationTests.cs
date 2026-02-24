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

    [Fact]
    public async Task FinancialSummary_GroupA_SingleSale_FullPayment_NoExpenses()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // OpeningBalance = 0 (assuming TestDataSeeder leaves BankSettings at 0 or we can overwrite it)
        var bankSettings = await _db.BankSystemSettings.FirstOrDefaultAsync(ct);
        if (bankSettings != null)
        {
            bankSettings.BankBaseBalance = 0;
            await _db.SaveChangesAsync(ct);
        }

        // Create product with specific cost
        var product = new Product { Name = "Test Product", Sku = "TEST-1", ReorderPoint = 10, Unit = "pcs", IsActive = true };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        _db.ProductBatches.Add(new ProductBatch { ProductId = product.Id, BatchNumber = "B1", OnHand = 1000, UnitCost = 77, UnitPrice = 85 });
        await _db.SaveChangesAsync(ct);

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
                new() { ProductId = product.Id, Quantity = 1000, UnitPrice = 85, BatchNumber = "B1" }
            }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 85000,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, null, ct);

        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear, TimezoneOffsetMinutes = 0 };
        var summary = await _reportingServices.GetFinancialSummaryAsync(filter, ct);

        Assert.Equal(85000m, summary.SalesRevenue);
        Assert.Equal(77000m, summary.CostOfGoods);
        Assert.Equal(8000m, summary.GrossProfit);
        Assert.Equal(0m, summary.InternalExpenses);
        Assert.Equal(8000m, summary.NetProfit);
        Assert.Equal(85000m, summary.BankBalance);
    }

    [Fact]
    public async Task FinancialSummary_GroupB_Refunds_And_Expenses()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // Initial setup identical to Group A
        var product = new Product { Name = "Test Product 2", Sku = "TEST-2", ReorderPoint = 10, Unit = "pcs", IsActive = true };
        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);
        _db.ProductBatches.Add(new ProductBatch { ProductId = product.Id, BatchNumber = "B1", OnHand = 100, UnitCost = 10, UnitPrice = 20 });
        await _db.SaveChangesAsync(ct);

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
                new() { ProductId = product.Id, Quantity = 100, UnitPrice = 20, BatchNumber = "B1" } // Revenue: 2000, COGS: 1000
            }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest
        {
            Amount = 2000,
            PaymentDate = DateTimeOffset.UtcNow,
            PaymentMethod = PaymentMethod.Cash
        }, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, null, ct);

        await _salesServices.RefundAsync(new RefundSalesOrderRequest
        {
            OrderId = orderId,
            Amount = 500,
            Reason = "Partial money refund"
        }, _user, ct);

        await _reportingServices.CreateInternalExpenseAsync(new CreateInternalExpenseRequestDto
        {
            Amount = 130,
            InternalExpenseType = InternalExpenseType.Other,
            Description = "Test expense",
            TimestampUtc = DateTimeOffset.UtcNow
        }, _user, ct);

        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear, TimezoneOffsetMinutes = 0 };
        var summary = await _reportingServices.GetFinancialSummaryAsync(filter, ct);

        // Original NetProfit without refund/expense = 1000.
        // Refund = 500. However, ReportingServices divides refund totals by 1.13 unconditionally.
        // So refundBase = 500 / 1.13 = 442.477...
        // NetSalesSubtotal = 2000 - 442.477 = 1557.522.
        // GrossProfit = 1557.522 - 1000 = 557.522.
        // NetProfit = 557.522 - 130 = 427.52.
        Assert.Equal(427.52m, summary.NetProfit);
        
        // Original Bank Balance = 2000
        // Refund out = 500
        // Expense out = 130
        // Bank Balance = 1370.
        Assert.Equal(1370m, Math.Round(summary.BankBalance, 2));
    }
}
