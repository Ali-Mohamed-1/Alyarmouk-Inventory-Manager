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
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Inventory.IntegrationTests;

public class SupplierBalanceIntegrationTests : IClassFixture<IntegrationTestFixture>, IDisposable
{
    private readonly IntegrationTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly AppDbContext _db;
    private readonly IReportingServices _reportingServices;
    private readonly IPurchaseOrderServices _purchaseServices;
    private readonly ISupplierSalesOrderServices _ssoServices;
    private readonly UserContext _user;

    public SupplierBalanceIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _scope = _fixture.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _reportingServices = _scope.ServiceProvider.GetRequiredService<IReportingServices>();
        _purchaseServices = _scope.ServiceProvider.GetRequiredService<IPurchaseOrderServices>();
        _ssoServices = _scope.ServiceProvider.GetRequiredService<ISupplierSalesOrderServices>();
        _user = new UserContext("test-user", "Test User");
        
        _db.Database.EnsureCreated();
        TestDataSeeder.SeedAsync(_db).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _scope.Dispose();
    }

    [Fact]
    public async Task SupplierBalance_DashboardMetrics_CalculateCorrectly()
    {
        var ct = CancellationToken.None;
        
        // Arrange
        // 1. Create a Supplier
        var supplier = new Supplier { Name = "Balance Test Supplier" };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);
        int supplierId = supplier.Id;

        // 2. Scenario Data Setup:
        // PO 1: $1000 Total, Paid $600. Remaining $400. Not Overdue.
        var po1Id = await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-10),
            DueDate = DateTimeOffset.UtcNow.AddDays(5),
            Status = PurchaseOrderStatus.Received,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 100 } }
        }, _user, ct);
        await _purchaseServices.AddPaymentAsync(po1Id, new Inventory.Application.DTOs.Payment.CreatePaymentRequest 
        { 
            Amount = 600, 
            PaymentDate = DateTimeOffset.UtcNow.AddDays(-5), 
            PaymentMethod = PaymentMethod.Cash 
        }, _user, ct);

        // PO 2: $500 Total, Unpaid. Remaining $500. Overdue (Due 2 days ago).
        var po2Id = await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-15),
            DueDate = DateTimeOffset.UtcNow.AddDays(-2),
            Status = PurchaseOrderStatus.Received,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 5, UnitPrice = 100 } }
        }, _user, ct);

        // PO 3: $300 Total, Cancelled. Should be ignored by metrics except TotalVolume? 
        // Actually, the rules say TotalVolume = sum of all POs. NetOwed = sum of non-cancelled POs.
        var po3Id = await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-5),
            DueDate = DateTimeOffset.UtcNow.AddDays(5),
            Status = PurchaseOrderStatus.Pending,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 3, UnitPrice = 100 } }
        }, _user, ct);
        await _purchaseServices.CancelAsync(po3Id, _user, ct);

        // SS O 1: $200. Active. Should reduce NetOwed but not affect Paid or Overdue.
        await _ssoServices.CreateAsync(new Inventory.Application.DTOs.SupplierSalesOrder.CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = DateTimeOffset.UtcNow,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<Inventory.Application.DTOs.SupplierSalesOrder.CreateSupplierSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 2, UnitPrice = 100 } // Total 200
            }
        }, _user, ct);

        // SS O 2: $150. Cancelled. Should have NO effect.
        var sso2Id = await _ssoServices.CreateAsync(new Inventory.Application.DTOs.SupplierSalesOrder.CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = DateTimeOffset.UtcNow,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<Inventory.Application.DTOs.SupplierSalesOrder.CreateSupplierSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 1.5m, UnitPrice = 100 } // Total 150
            }
        }, _user, ct);
        await _ssoServices.CancelAsync(sso2Id, _user, ct);

        // Act
        var balance = await _reportingServices.GetSupplierBalanceAsync(supplierId, ct);

        // Assert
        // Expected Metrics:
        // Total Volume: PO1(1000) + PO2(500) + PO3(300) = $1800
        // Paid: PO1 Payment(600) = $600
        // Overdue: PO2 Remaining(500) = $500
        // NetOwedToSupplier: (PO1 Remaining(400) + PO2 Remaining(500)) - (SSO1 Active(200)) = 900 - 200 = $700
        
        Assert.Equal(1800, balance.TotalVolume);
        Assert.Equal(600, balance.Paid);
        Assert.Equal(500, balance.Overdue);
        Assert.Equal(700, balance.NetOwedToSupplier);
    }

    [Fact]
    public async Task SupplierBalance_IsolationTest()
    {
        var ct = CancellationToken.None;

        // Arrange
        var supplier = new Supplier { Name = "Isolation Test Supplier" };
        _db.Suppliers.Add(supplier);
        await _db.SaveChangesAsync(ct);
        int supplierId = supplier.Id;

        // PO: $1000, Unpaid, Overdue.
        await _purchaseServices.CreateAsync(new CreatePurchaseOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = DateTimeOffset.UtcNow.AddDays(-10),
            DueDate = DateTimeOffset.UtcNow.AddDays(-1),
            Status = PurchaseOrderStatus.Received,
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 10, UnitPrice = 100 } }
        }, _user, ct);

        // SSO: $1000 (Same as PO). 
        // This should clear NetOwed, but Paid and Overdue should stay as they are (raw cash metrics).
        await _ssoServices.CreateAsync(new Inventory.Application.DTOs.SupplierSalesOrder.CreateSupplierSalesOrderRequest
        {
            SupplierId = supplierId,
            OrderDate = DateTimeOffset.UtcNow,
            DueDate = DateTimeOffset.UtcNow.AddDays(7),
            ApplyVat = false,
            ApplyManufacturingTax = false,
            Lines = new List<Inventory.Application.DTOs.SupplierSalesOrder.CreateSupplierSalesOrderLineRequest>
            {
                new() { ProductId = 1, Quantity = 10, UnitPrice = 100 }
            }
        }, _user, ct);

        // Act
        var balance = await _reportingServices.GetSupplierBalanceAsync(supplierId, ct);

        // Assert
        Assert.Equal(0, balance.NetOwedToSupplier);
        Assert.Equal(0, balance.Paid);
        Assert.Equal(1000, balance.Overdue); // Overdue remains 1000 because SSO is not a cash payment
    }
}
