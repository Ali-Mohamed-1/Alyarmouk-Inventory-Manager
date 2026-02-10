using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.IntegrationTests;

/// <summary>
/// Seeds minimal reference data for integration tests.
/// Call after EnsureCreated on a fresh DbContext.
/// </summary>
public static class TestDataSeeder
{
    /// <summary>
    /// Clears all data and re-seeds. Use at the start of tests that need a clean slate.
    /// </summary>
    public static async Task ResetAndSeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        await ClearAllDataAsync(db, ct);
        await SeedInternalAsync(db, ct);
    }

    /// <summary>
    /// Seeds minimal reference data. Skips if data already exists.
    /// </summary>
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Customers.AnyAsync(ct)) return;
        await SeedInternalAsync(db, ct);
    }

    private static async Task ClearAllDataAsync(AppDbContext db, CancellationToken ct)
    {
        // Order matters: children before parents
        db.RefundTransactionLines.RemoveRange(await db.RefundTransactionLines.ToListAsync(ct));
        db.RefundTransactions.RemoveRange(await db.RefundTransactions.ToListAsync(ct));
        db.PaymentRecords.RemoveRange(await db.PaymentRecords.ToListAsync(ct));
        db.InventoryTransactions.RemoveRange(await db.InventoryTransactions.ToListAsync(ct));
        db.SalesOrderLines.RemoveRange(await db.SalesOrderLines.ToListAsync(ct));
        db.PurchaseOrderLines.RemoveRange(await db.PurchaseOrderLines.ToListAsync(ct));
        db.SalesOrders.RemoveRange(await db.SalesOrders.ToListAsync(ct));
        db.PurchaseOrders.RemoveRange(await db.PurchaseOrders.ToListAsync(ct));
        db.ProductBatches.RemoveRange(await db.ProductBatches.ToListAsync(ct));
        db.StockSnapshots.RemoveRange(await db.StockSnapshots.ToListAsync(ct));
        db.FinancialTransactions.RemoveRange(await db.FinancialTransactions.ToListAsync(ct));
        db.AuditLogs.RemoveRange(await db.AuditLogs.ToListAsync(ct));
        db.Products.RemoveRange(await db.Products.ToListAsync(ct));
        db.Customers.RemoveRange(await db.Customers.ToListAsync(ct));
        db.Suppliers.RemoveRange(await db.Suppliers.ToListAsync(ct));
        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedInternalAsync(AppDbContext db, CancellationToken ct)
    {

        var product = new Product
        {
            Id = 1,
            Name = "Product A",
            Sku = "SKU-A",
            Unit = "PCS",
            IsActive = true,
            ReorderPoint = 0
        };
        db.Products.Add(product);

        db.StockSnapshots.Add(new StockSnapshot { ProductId = 1, OnHand = 0 });

        var batch = new ProductBatch
        {
            Id = 100,
            ProductId = 1,
            BatchNumber = "BATCH-001",
            UnitCost = 80,
            UnitPrice = 100,
            OnHand = 50,
            Reserved = 0
        };
        db.ProductBatches.Add(batch);

        db.Customers.Add(new Customer { Id = 1, Name = "Test Customer" });
        db.Suppliers.Add(new Supplier { Id = 1, Name = "Test Supplier" });

        await db.SaveChangesAsync(ct);
    }
}
