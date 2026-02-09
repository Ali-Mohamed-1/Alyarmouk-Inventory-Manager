using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.Reporting;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class HistoricalOrdersIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly ISalesOrderServices _salesServices;
    private readonly IPurchaseOrderServices _purchaseServices;
    private readonly IReportingServices _reportingServices;
    private readonly UserContext _user;

    public HistoricalOrdersIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _salesServices = _scope.ServiceProvider.GetRequiredService<ISalesOrderServices>();
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
    public async Task Category1_SalesOrder_PersistedExactlyAsEntered()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create Sales Order (Done, Paid, No Taxes for simple assertion)
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            PaymentStatus = PaymentStatus.Paid,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 10, UnitPrice = 100, BatchNumber = "BATCH-001" }
            }
        };

        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        // 2. Reload and verify
        _db.ChangeTracker.Clear();
        var order = await _db.SalesOrders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        
        Assert.NotNull(order);
        Assert.True(order.IsHistorical);
        Assert.Equal(SalesOrderStatus.Done, order.Status);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(1000, order.TotalAmount);
        Assert.Equal(1000, order.GetTotalPaid());
    }

    [Fact]
    public async Task Category1_PurchaseOrder_PersistedExactlyAsEntered()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create Purchase Order (Received, Paid, No Taxes)
        var createReq = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = PurchaseOrderStatus.Received,
            PaymentStatus = PurchasePaymentStatus.Paid,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 10, UnitPrice = 80, BatchNumber = "BATCH-PO-HIST" }
            }
        };

        var orderId = await _purchaseServices.CreateAsync(createReq, _user, ct);

        // 2. Reload and verify
        _db.ChangeTracker.Clear();
        var order = await _db.PurchaseOrders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId, ct);

        Assert.NotNull(order);
        Assert.True(order.IsHistorical);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Equal(PurchasePaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(800, order.TotalAmount);
        Assert.Equal(800, order.GetTotalPaid());
    }

    [Fact]
    public async Task Category2_StockIsolation_OnCreation()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // Initial stock (seeded as 50)
        var initialBatch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-001", ct);
        Assert.Equal(50, initialBatch.OnHand);
        Assert.Equal(0, initialBatch.Reserved);

        // 1. Create Historical Sales Order (Done)
        var salesReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            Lines = new List<CreateSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 10, UnitPrice = 100, BatchNumber = "BATCH-001" }
            }
        };
        await _salesServices.CreateAsync(salesReq, _user, ct);

        // 2. Create Historical Purchase Order (Received)
        var purchaseReq = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = PurchaseOrderStatus.Received,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 20, UnitPrice = 80, BatchNumber = "BATCH-PO-NEW" }
            }
        };
        await _purchaseServices.CreateAsync(purchaseReq, _user, ct);

        // 3. Assert stock UNCHANGED
        _db.ChangeTracker.Clear();
        initialBatch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-001", ct);
        Assert.Equal(50, initialBatch.OnHand);
        Assert.Equal(0, initialBatch.Reserved);

        var newBatch = await _db.ProductBatches.FirstOrDefaultAsync(b => b.BatchNumber == "BATCH-PO-NEW", ct);
        Assert.Null(newBatch); // Purchase order received should create batch normally, 
                               // but historical creation should be skipped.

        var stockMovements = await _db.InventoryTransactions.CountAsync(ct);
        Assert.Equal(0, stockMovements);
    }

    [Fact]
    public async Task Category3_StockActivation_OnEdit()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create Historical Sales Order (Done)
        var salesReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            Lines = new List<CreateSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 10, UnitPrice = 100, BatchNumber = "BATCH-001" }
            }
        };
        var salesId = await _salesServices.CreateAsync(salesReq, _user, ct);

        // 2. Activate Stock for Sales Order
        await _salesServices.ActivateStockAsync(salesId, _user, ct);

        // Assert stock deducted
        _db.ChangeTracker.Clear();
        var batch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-001", ct);
        Assert.Equal(40, batch.OnHand);
        Assert.Equal(0, batch.Reserved);

        // 3. Create Historical Purchase Order (Received)
        var purchaseReq = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = PurchaseOrderStatus.Received,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 20, UnitPrice = 80, BatchNumber = "BATCH-PO-ACTIVE" }
            }
        };
        var purchaseId = await _purchaseServices.CreateAsync(purchaseReq, _user, ct);

        // 4. Activate Stock for Purchase Order
        await _purchaseServices.ActivateStockAsync(purchaseId, _user, ct);

        // Assert stock added
        _db.ChangeTracker.Clear();
        var poBatch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-PO-ACTIVE", ct);
        Assert.Equal(20, poBatch.OnHand);

        // Total movements
        var stockMovements = await _db.InventoryTransactions.ToListAsync(ct);
        Assert.NotEmpty(stockMovements);
    }

    [Fact]
    public async Task Category4_CustomerSupplierHistory_Impact()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create historical Sales Order (PartiallyPaid, Overdue, No Taxes)
        var pastDate = DateTimeOffset.UtcNow.AddDays(-30);
        var salesReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            OrderDate = pastDate,
            DueDate = pastDate.AddDays(7), // Still in the past
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 200, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(salesReq, _user, ct);

        // Record partial payment
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 400, PaymentDate = pastDate, PaymentMethod = PaymentMethod.Cash }, _user, ct);

        // 2. Assert Customer History
        var balance = await _reportingServices.GetCustomerBalanceAsync(1, DateTimeOffset.UtcNow, ct);
        Assert.Equal(600, balance.TotalPending);
        Assert.Equal(600, balance.Deserved);
        Assert.True(balance.Deserved > 0);

        // 3. Create historical Purchase Order (PartiallyPaid, No Taxes)
        var purchaseReq = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            OrderDate = pastDate,
            DueDate = pastDate.AddDays(7),
            IsHistorical = true,
            Status = PurchaseOrderStatus.Received,
            PaymentStatus = PurchasePaymentStatus.Unpaid,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 80, BatchNumber = "BATCH-PO-HIST" } }
        };
        var poId = await _purchaseServices.CreateAsync(purchaseReq, _user, ct);

        await _purchaseServices.AddPaymentAsync(poId, new CreatePaymentRequest { Amount = 300, PaymentDate = pastDate, PaymentMethod = PaymentMethod.Cash }, _user, ct);

        // 4. Assert Supplier History
        var supplierBalance = await _reportingServices.GetSupplierBalanceAsync(1, ct);
        Assert.Equal(500, supplierBalance.TotalPending);
    }

    [Fact]
    public async Task Category5_RemainingOverdueMoney_Calculations()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create Sales Order (Pending, Due in the past, No Taxes)
        var pastDate = DateTimeOffset.UtcNow.AddDays(-10);
        var salesReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            OrderDate = pastDate,
            DueDate = pastDate.AddDays(1).AddSeconds(-1), // Due 9 days ago
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            PaymentStatus = PaymentStatus.Pending,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 500, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(salesReq, _user, ct);

        // 2. Query Details
        var details = await _salesServices.GetByIdAsync(orderId, ct);
        Assert.NotNull(details);
        Assert.True(details.IsOverdue);
        Assert.Equal(500, details.RemainingAmount);
        Assert.Equal(500, details.DeservedAmount);

        // 3. Pay Partially
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest { Amount = 200, PaymentDate = DateTimeOffset.UtcNow, PaymentMethod = PaymentMethod.Cash }, _user, ct);

        details = await _salesServices.GetByIdAsync(orderId, ct);
        Assert.Equal(300, details.RemainingAmount);
        Assert.Equal(300, details.DeservedAmount);
    }

    [Fact]
    public async Task Category6_FinancialSummary_Impact()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create Paid Sales Order with Taxes
        var salesReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            PaymentStatus = PaymentStatus.Paid,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = true,
            ApplyManufacturingTax = true,
            IsTaxInclusive = false, // Base + Taxes
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 200, BatchNumber = "BATCH-001" } }
        };
        // Base = 2000, VAT = 280 (14%), ManTax = 20 (1%). Total = 2260.
        await _salesServices.CreateAsync(salesReq, _user, ct);

        // 2. Create Paid Purchase Order with Taxes
        var purchaseReq = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = PurchaseOrderStatus.Received,
            PaymentStatus = PurchasePaymentStatus.Paid,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = true,
            ApplyManufacturingTax = true,
            IsTaxInclusive = false,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 100, BatchNumber = "BATCH-PO-HIST" } }
        };
        // Base = 1000, VAT = 140 (14%), ManTax = 10 (1%). Total = 1130.
        await _purchaseServices.CreateAsync(purchaseReq, _user, ct);

        // 3. Verify Financial Summary
        var summary = await _reportingServices.GetFinancialSummaryAsync(new FinancialReportFilterDto { DateRangeType = FinancialDateRangeType.ThisYear }, ct);
        
        Assert.Equal(140, summary.TotalVat); // 280 - 140
        Assert.Equal(10, summary.TotalManufacturingTax); // 20 - 10
        Assert.Equal(1130, summary.PurchasePayments);
        Assert.Equal(980, summary.BankBalance); // (2260 - 1130) - 140 - 10
    }

    [Fact]
    public async Task Category7_EditingIntegrity_UpdateStatusAndPayment()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create Historical Pending Order (No Taxes)
        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = SalesOrderStatus.Pending,
            PaymentStatus = PaymentStatus.Pending,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 1000, BatchNumber = "BATCH-001" } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        // 2. Add Payment and Change Status
        await _salesServices.AddPaymentAsync(orderId, new CreatePaymentRequest 
        { 
            Amount = 1000, 
            PaymentDate = DateTimeOffset.UtcNow, 
            PaymentMethod = PaymentMethod.Cash 
        }, _user, ct);
        await _salesServices.UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, _user, ct: ct);

        // 3. Verify Ledger and Stock
        _db.ChangeTracker.Clear();
        var ledgerEntries = await _db.FinancialTransactions.Where(t => t.SalesOrderId == orderId).ToListAsync(ct);
        Assert.Contains(ledgerEntries, t => t.Type == FinancialTransactionType.Revenue && t.Amount == 1000);

        var batch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-001", ct);
        Assert.Equal(49, batch.OnHand);
        
        var order = await _db.SalesOrders.FirstAsync(o => o.Id == orderId, ct);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public async Task Category8_Regression_PaidHistoricalNotSavedAsUnpaid()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            PaymentStatus = PaymentStatus.Paid,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100 } }
        };
        var orderId = await _salesServices.CreateAsync(createReq, _user, ct);

        _db.ChangeTracker.Clear();
        var order = await _db.SalesOrders.FirstAsync(o => o.Id == orderId, ct);
        Assert.Equal(PaymentStatus.Paid, order.PaymentStatus);
    }

    [Fact]
    public async Task Category8_Regression_HistoricalOrderDoesNotAffectStockOnCreation()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        var createReq = new CreateSalesOrderRequest
        {
            CustomerId = 1,
            DueDate = DateTimeOffset.UtcNow,
            IsHistorical = true,
            Status = SalesOrderStatus.Done,
            Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 100, BatchNumber = "BATCH-001" } }
        };
        await _salesServices.CreateAsync(createReq, _user, ct);

        var batch = await _db.ProductBatches.FirstAsync(b => b.BatchNumber == "BATCH-001", ct);
        Assert.Equal(50, batch.OnHand);
    }

    [Fact]
    public async Task Category8_Regression_PaidHistoricalPurchaseOrderSavedAsPaid()
    {
        var ct = CancellationToken.None;
        await TestDataSeeder.ResetAndSeedAsync(_db, ct);

        // 1. Create Historical Purchase Order as PAID
        var createReq = new CreatePurchaseOrderRequest
        {
            SupplierId = 1,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            IsHistorical = true,
            Status = PurchaseOrderStatus.Received,
            PaymentStatus = PurchasePaymentStatus.Paid,
            PaymentMethod = PaymentMethod.Cash,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 1000 } }
        };
        var orderId = await _purchaseServices.CreateAsync(createReq, _user, ct);

        // 2. Verify in DB
        _db.ChangeTracker.Clear();
        var order = await _db.PurchaseOrders.Include(o => o.Payments).FirstOrDefaultAsync(o => o.Id == orderId, ct);
        
        Assert.NotNull(order);
        Assert.Equal(PurchasePaymentStatus.Paid, order.PaymentStatus);
        Assert.Equal(1000, order.TotalAmount);
        Assert.Equal(1000, order.GetTotalPaid());
        Assert.Single(order.Payments);
    }
}
