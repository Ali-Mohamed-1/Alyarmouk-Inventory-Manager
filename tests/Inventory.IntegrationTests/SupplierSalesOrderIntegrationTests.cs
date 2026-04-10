using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.Reporting;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.SupplierSalesOrder;
using Inventory.Application.Exceptions;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CreatePaymentRequest = Inventory.Application.DTOs.Payment.CreatePaymentRequest;

namespace Inventory.IntegrationTests;

public class SupplierSalesOrderIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISupplierSalesOrderServices _ssoServices;
    private readonly IPurchaseOrderServices _purchaseServices;
    private readonly ISalesOrderServices _salesServices;
    private readonly IReportingServices _reportingServices;
    private readonly UserContext _user;

    public SupplierSalesOrderIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _ssoServices = _scope.ServiceProvider.GetRequiredService<ISupplierSalesOrderServices>();
        _purchaseServices = _scope.ServiceProvider.GetRequiredService<IPurchaseOrderServices>();
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

    private async Task<int> PrepareSupplierWithDebt(decimal debtAmount)
    {
        var supplier = new Supplier { Name = $"SSO Test Supplier {Guid.NewGuid()}" };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        var poReq = new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-1),
            DueDate = DateTimeOffset.UtcNow.AddDays(10),
            Status = PurchaseOrderStatus.Received,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = debtAmount / 10m, UnitPrice = 10m }
            }
        };
        await _purchaseServices.CreateAsync(poReq, _user);
        return supplier.Id;
    }

    // 1. Create order within NetOwedToSupplier → Status = Active, P&L affected
    [Fact]
    public async Task Create_OrderWithinNetOwed_ShouldSucceed()
    {
        var supplierId = await PrepareSupplierWithDebt(1000m);

        var req = new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } // 500
            }
        };

        var id = await _ssoServices.CreateAsync(req, _user);
        var order = await _ssoServices.GetByIdAsync(id);

        Assert.NotNull(order);
        Assert.Equal(SalesOrderStatus.Pending, order.Status); // Pending is "Active" in this context
    }

    // 2. Create order exceeding NetOwedToSupplier → blocked, clear error returned
    [Fact]
    public async Task Create_OrderExceedingNetOwed_ShouldThrowValidationException()
    {
        var supplierId = await PrepareSupplierWithDebt(500m);

        var req = new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 60, UnitPrice = 10m } // 600
            }
        };

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _ssoServices.CreateAsync(req, _user));
        Assert.Contains("Supplier balance is only", ex.Message);
    }

    // 3. Create two orders that together exceed NetOwedToSupplier → second one blocked
    [Fact]
    public async Task Create_TwoOrdersExceedingNetOwed_SecondShouldThrow()
    {
        var supplierId = await PrepareSupplierWithDebt(1000m);

        await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 60, UnitPrice = 10m } } // 600
        }, _user);

        var req2 = new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } } // 500
        };

        await Assert.ThrowsAsync<ValidationException>(() => _ssoServices.CreateAsync(req2, _user));
    }

    // 4. Cancel Active order → Status = Cancelled, P&L reversed, no ledger writes
    [Fact]
    public async Task Cancel_ActiveOrder_ShouldSetToCancelled_NoLedgerWrites()
    {
        var supplierId = await PrepareSupplierWithDebt(1000m);
        var req = new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } } // 500
        };
        var id = await _ssoServices.CreateAsync(req, _user);

        var txCountBefore = await _db.FinancialTransactions.Where(t => t.SupplierSalesOrderId == id).CountAsync();
        Assert.Equal(2, txCountBefore); // Created with revenue and COGS

        await _ssoServices.CancelAsync(id, _user);

        var order = await _ssoServices.GetByIdAsync(id);
        Assert.Equal(SalesOrderStatus.Cancelled, order.Status);

        var txCountAfter = await _db.FinancialTransactions.Where(t => t.SupplierSalesOrderId == id).CountAsync();
        Assert.Equal(txCountBefore, txCountAfter); // No new ledger writes
    }

    // 5. Cancel already Cancelled order → blocked
    [Fact]
    public async Task Cancel_AlreadyCancelledOrder_ShouldThrow()
    {
        var supplierId = await PrepareSupplierWithDebt(1000m);
        var req = new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } }
        };
        var id = await _ssoServices.CreateAsync(req, _user);
        
        await _ssoServices.CancelAsync(id, _user);

        await Assert.ThrowsAsync<ValidationException>(() => _ssoServices.CancelAsync(id, _user));
    }

    // 6. Create order 500 → NetSalesRevenue increases by 500
    [Fact]
    public async Task CreateOrder_500_IncreasesNetSalesRevenueBy500()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        var supplierId = await PrepareSupplierWithDebt(1000m);
        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear };
        var before = await _reportingServices.GetFinancialSummaryAsync(filter);

        var req = new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } } // 500
        };
        await _ssoServices.CreateAsync(req, _user);

        var after = await _reportingServices.GetFinancialSummaryAsync(filter);
        Assert.Equal(before.SalesRevenue + 500m, after.SalesRevenue);
    }

    // 7. Create order 500 → COGS increases by order cost
    [Fact]
    public async Task CreateOrder_500_IncreasesCOGS()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        
        // ensure product batch cost
        var pId = await _db.Products.Select(p => p.Id).FirstAsync();
        var batch = new ProductBatch { ProductId = pId, BatchNumber = Guid.NewGuid().ToString(), UnitCost = 8m, OnHand = 100 };
        _db.ProductBatches.Add(batch);
        await _db.SaveChangesAsync();

        var supplierId = await PrepareSupplierWithDebt(1000m);
        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear };
        var before = await _reportingServices.GetFinancialSummaryAsync(filter);

        var req = new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = pId, ProductBatchId = batch.Id, Quantity = 50, UnitPrice = 10m } } // 500
        };
        await _ssoServices.CreateAsync(req, _user);

        var after = await _reportingServices.GetFinancialSummaryAsync(filter);
        Assert.Equal(before.CostOfGoods + 400m, after.CostOfGoods); // 50 * 8 = 400 cost
    }

    // 8. Cancel order 500 → NetSalesRevenue reversed
    [Fact]
    public async Task CancelOrder_500_ReversesNetSalesRevenue()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        var supplierId = await PrepareSupplierWithDebt(1000m);
        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear };
        
        var id = await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } } // 500
        }, _user);

        var beforeCancel = await _reportingServices.GetFinancialSummaryAsync(filter);
        
        await _ssoServices.CancelAsync(id, _user);

        var afterCancel = await _reportingServices.GetFinancialSummaryAsync(filter);
        Assert.Equal(beforeCancel.SalesRevenue - 500m, afterCancel.SalesRevenue);
    }

    // 9. BankBalance unchanged after creation
    [Fact]
    public async Task CreateOrder_BankBalanceUnchanged()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        var supplierId = await PrepareSupplierWithDebt(1000m);
        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear };
        var before = await _reportingServices.GetFinancialSummaryAsync(filter);

        await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } }
        }, _user);

        var after = await _reportingServices.GetFinancialSummaryAsync(filter);
        Assert.Equal(before.BankBalance, after.BankBalance);
    }

    // 10. BankBalance unchanged after cancellation
    [Fact]
    public async Task CancelOrder_BankBalanceUnchanged()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        var supplierId = await PrepareSupplierWithDebt(1000m);
        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear };
        
        var id = await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } }
        }, _user);

        var beforeCancel = await _reportingServices.GetFinancialSummaryAsync(filter);
        await _ssoServices.CancelAsync(id, _user);

        var afterCancel = await _reportingServices.GetFinancialSummaryAsync(filter);
        Assert.Equal(beforeCancel.BankBalance, afterCancel.BankBalance);
    }

    // 11. Purchase 1000, supplier sale 500 Active → NetOwed = 500
    [Fact]
    public async Task Purchase1000_Sale500Active_NetOwedIs500()
    {
        var supplierId = await PrepareSupplierWithDebt(1000m);
        
        await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } } // 500
        }, _user);

        var bal = await _reportingServices.GetSupplierBalanceAsync(supplierId);
        Assert.Equal(500m, bal.NetOwedToSupplier);
    }

    // 12. Purchase 1000, supplier sale 500 Cancelled → NetOwed = 1000
    [Fact]
    public async Task Purchase1000_Sale500Cancelled_NetOwedIs1000()
    {
        var supplierId = await PrepareSupplierWithDebt(1000m);
        
        var id = await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } } // 500
        }, _user);

        await _ssoServices.CancelAsync(id, _user);

        var bal = await _reportingServices.GetSupplierBalanceAsync(supplierId);
        Assert.Equal(1000m, bal.NetOwedToSupplier);
    }

    // 13. Purchase 1000, two supplier sales 300 + 200 Active → NetOwed = 500
    [Fact]
    public async Task Purchase1000_TwoSalesActive_NetOwedIs500()
    {
        var supplierId = await PrepareSupplierWithDebt(1000m);
        
        await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 30, UnitPrice = 10m } } // 300
        }, _user);

        await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 20, UnitPrice = 10m } } // 200
        }, _user);

        var bal = await _reportingServices.GetSupplierBalanceAsync(supplierId);
        Assert.Equal(500m, bal.NetOwedToSupplier);
    }

    // 14. Paid metric reflects cash payments only, not supplier sales
    [Fact]
    public async Task SupplierBalance_PaidMetric_ReflectsCashOnly()
    {
        var supplier = new Supplier { Name = $"SSO Test Supplier {Guid.NewGuid()}" };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        var poId = await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id, OrderDate = DateTimeOffset.UtcNow, DueDate = DateTimeOffset.UtcNow.AddDays(10),
            Status = PurchaseOrderStatus.Received, Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 100, UnitPrice = 10m } } // 1000
        }, _user);

        await _purchaseServices.AddPaymentAsync(poId, new CreatePaymentRequest { Amount = 100, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user);

        await _ssoServices.CreateAsync(new CreateSupplierSalesOrderRequest
        {
            SupplierId = supplier.Id, DueDate = DateTimeOffset.UtcNow.AddDays(7), ApplyVat = false, ApplyManufacturingTax = false,
            Lines = new List<CreateSupplierSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 50, UnitPrice = 10m } } // 500
        }, _user);

        var bal = await _reportingServices.GetSupplierBalanceAsync(supplier.Id);
        Assert.Equal(100m, bal.Paid); // Should be 100, not 600
        Assert.Equal(400m, bal.NetOwedToSupplier); // 1000 - 100 cash - 500 SSO = 400
    }

    // 15. Purchase order past DueDate, not Paid/Cancelled → appears in Overdue
    [Fact]
    public async Task Overdue_POPastDueDate_AppearsInOverdue()
    {
        var supplier = new Supplier { Name = $"SSO Test Supplier {Guid.NewGuid()}" };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id, OrderDate = DateTimeOffset.UtcNow.AddDays(-10), DueDate = DateTimeOffset.UtcNow.AddDays(-1), // Past due
            Status = PurchaseOrderStatus.Received, Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 100, UnitPrice = 10m } } // 1000
        }, _user);

        var bal = await _reportingServices.GetSupplierBalanceAsync(supplier.Id);
        Assert.Equal(1000m, bal.Overdue);
    }

    // 16. Paid purchase order → excluded from Overdue
    [Fact]
    public async Task Overdue_PaidPO_ExcludedFromOverdue()
    {
         var supplier = new Supplier { Name = $"SSO Test Supplier {Guid.NewGuid()}" };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        var poId = await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id, OrderDate = DateTimeOffset.UtcNow.AddDays(-10), DueDate = DateTimeOffset.UtcNow.AddDays(-1),
            Status = PurchaseOrderStatus.Received, Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 100, UnitPrice = 10m } } // 1000
        }, _user);

        await _purchaseServices.AddPaymentAsync(poId, new CreatePaymentRequest { Amount = 1000, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user);

        var bal = await _reportingServices.GetSupplierBalanceAsync(supplier.Id);
        Assert.Equal(0m, bal.Overdue);
    }

    // 17. Existing customer order payment flow unchanged
    [Fact]
    public async Task Regression_CustomerOrderPaymentFlow_ChangesBankBalance()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear };
        var summaryBefore = await _reportingServices.GetFinancialSummaryAsync(filter);
        
        var soId = await _salesServices.CreateAsync(new CreateSalesOrderRequest
        {
            CustomerId = 1, DueDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash, PaymentStatus = PaymentStatus.Pending,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 100m, BatchNumber = "BATCH-001" } }
        }, _user);

        await _salesServices.AddPaymentAsync(soId, new CreatePaymentRequest { Amount = 1000, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user);

        var summaryAfter = await _reportingServices.GetFinancialSummaryAsync(filter);
        Assert.Equal(summaryBefore.BankBalance + 1000m, summaryAfter.BankBalance);
    }

    // 18. Existing customer refund flow unchanged
    [Fact]
    public async Task Regression_CustomerRefundFlow_ChangesBankBalance()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        
        var soId = await _salesServices.CreateAsync(new CreateSalesOrderRequest
        {
            CustomerId = 1, DueDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash, PaymentStatus = PaymentStatus.Pending,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 100m, BatchNumber = "BATCH-001" } }
        }, _user);
        await _salesServices.AddPaymentAsync(soId, new CreatePaymentRequest { Amount = 1000, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user);

        var filter = new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear };
        var summaryBefore = await _reportingServices.GetFinancialSummaryAsync(filter);

        await _salesServices.RefundAsync(new RefundSalesOrderRequest { OrderId = soId, Amount = 100m, Reason = "Regression test refund" }, _user);

        var summaryAfter = await _reportingServices.GetFinancialSummaryAsync(filter);
        Assert.Equal(summaryBefore.BankBalance - 100m, summaryAfter.BankBalance);
    }

    // 19. Existing customer cancellation flow unchanged
    [Fact]
    public async Task Regression_CustomerCancellationFlow_WorksNormally()
    {
        await TestDataSeeder.ResetAndSeedAsync(_db, CancellationToken.None);
        var soId = await _salesServices.CreateAsync(new CreateSalesOrderRequest
        {
            CustomerId = 1, DueDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash, PaymentStatus = PaymentStatus.Pending,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 100m, BatchNumber = "BATCH-001" } }
        }, _user);

        await _salesServices.CancelAsync(soId, _user);
        var soDto = await _salesServices.GetByIdAsync(soId);
        Assert.Equal(SalesOrderStatus.Cancelled, soDto!.Status);
    }

    // 20. Existing supplier balance (purchases only) unaffected
    [Fact]
    public async Task Regression_SupplierBalance_NoSSO_Unaffected()
    {
        var supplier = new Supplier { Name = $"SSO Test Supplier {Guid.NewGuid()}" };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync();

        await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplier.Id, OrderDate = DateTimeOffset.UtcNow, DueDate = DateTimeOffset.UtcNow.AddDays(10),
            Status = PurchaseOrderStatus.Received, Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 100, UnitPrice = 10m } } // 1000
        }, _user);

        var bal = await _reportingServices.GetSupplierBalanceAsync(supplier.Id);
        Assert.Equal(1000m, bal.NetOwedToSupplier);
        Assert.Equal(1000m, bal.TotalVolume);
        Assert.Equal(0m, bal.Paid);
    }
}
