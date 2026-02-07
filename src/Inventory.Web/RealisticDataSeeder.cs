using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Customer;
using Inventory.Application.DTOs.Product;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.Supplier;
using Inventory.Application.DTOs.Transaction;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Web;

public static class RealisticDataSeeder
{
    private static readonly Random _rng = new Random();

    public static async Task SeedRealisticDataAsync(IServiceProvider services)
    {
        var _user = new Inventory.Application.DTOs.UserContext("seed-user", "System Seeder");
        var db = services.GetRequiredService<AppDbContext>();
        
        // 1. CLEAR EXISTING DATA
        await ClearDatabaseAsync(db);

        // 2. GET SERVICES
        var categoryParams = services.GetRequiredService<ICategoryServices>();
        var supplierParams = services.GetRequiredService<ISupplierServices>();
        var customerParams = services.GetRequiredService<ICustomerServices>();
        var productParams = services.GetRequiredService<IProductServices>();
        var poParams = services.GetRequiredService<IPurchaseOrderServices>();
        var soParams = services.GetRequiredService<ISalesOrderServices>();
        var inventoryParams = services.GetRequiredService<IInventoryTransactionServices>();

        // 3. MASTER DATA
        var categories = await SeedCategories(categoryParams, _user);
        var suppliers = await SeedSuppliers(supplierParams, _user);
        var customers = await SeedCustomers(customerParams, _user);
        var products = await SeedProducts(productParams, categories, _user);

        // 3.5 INITIAL STOCK BOOST (Ensure 2-3 batches per product)
        Console.WriteLine("Boosting initial stock for all products...");
        foreach (var prod in products)
        {
            var date1 = DateTimeOffset.UtcNow.AddMonths(-11);
            var date2 = DateTimeOffset.UtcNow.AddMonths(-9);
            
            await CreateInitialBatch(poParams, db, prod, suppliers[_rng.Next(suppliers.Count)], date1, _user);
            await CreateInitialBatch(poParams, db, prod, suppliers[_rng.Next(suppliers.Count)], date2, _user);
        }

        // 4. TRANSACTIONS GENERATION (Time-based simulation)
        var startDate = DateTimeOffset.UtcNow.AddYears(-1).AddMonths(4); // Start after initial boost
        var currentDate = startDate;
        var today = DateTimeOffset.UtcNow;

        var activeBatches = new List<(int ProductId, string BatchNum, int QtyRemaining)>();

        int poCount = 0;
        int soCount = 0;

        Console.WriteLine("Starting transaction simulation...");

        while (currentDate < today.AddDays(15)) 
        {
            // 4a. PURCHASE ORDERS (Restocking)
            if (_rng.NextDouble() < 0.05) // Reduced from 0.2 to 0.05
            {
                var supplier = suppliers[_rng.Next(suppliers.Count)];
                var numLines = _rng.Next(1, 4);
                var lines = new List<CreatePurchaseOrderLineRequest>();
                
                for (int i = 0; i < numLines; i++)
                {
                    var prod = products[_rng.Next(products.Count)];
                    if (lines.Any(l => l.ProductId == prod.Id)) continue;

                    var qty = _rng.Next(10, 50) * 10; 
                    var cost = 10 + (decimal)(_rng.NextDouble() * 100);

                    lines.Add(new CreatePurchaseOrderLineRequest
                    {
                        ProductId = prod.Id,
                        Quantity = qty,
                        UnitPrice = Math.Round(cost, 2),
                        BatchNumber = $"B{currentDate:yyMMdd}-{prod.Sku}-{_rng.Next(100, 999)}"
                    });
                }

                if (lines.Any())
                {
                    var dueDate = currentDate.AddDays(_rng.Next(7, 20));
                    var poReq = new CreatePurchaseOrderRequest
                    {
                        SupplierId = supplier.Id,
                        DueDate = dueDate,
                        IsTaxInclusive = _rng.NextDouble() > 0.5,
                        Lines = lines,
                        ConnectToReceiveStock = true 
                    };

                    var poId = await poParams.CreateAsync(poReq, _user);
                    poCount++;

                    if (dueDate < today)
                    {
                        await poParams.UpdateStatusAsync(poId, PurchaseOrderStatus.Received, _user, currentDate); // FIX: pass simulation date

                        // Set Selling Price for these new batches and Link Supplier to Product
                        foreach(var line in lines)
                        {
                            var batch = await db.ProductBatches.FirstOrDefaultAsync(b => b.ProductId == line.ProductId && b.BatchNumber == line.BatchNumber);
                            if (batch != null)
                            {
                                batch.UnitPrice = Math.Round(line.UnitPrice * 2.0m, 2); // Increased margin
                            }

                            // LINK SUPPLIER TO PRODUCT
                            var dbSup = await db.Suppliers.Include(s => s.Products).FirstOrDefaultAsync(s => s.Id == supplier.Id);
                            if (dbSup != null && !dbSup.Products.Any(p => p.Id == line.ProductId))
                            {
                                var dbProd = await db.Products.FindAsync(line.ProductId);
                                if (dbProd != null) dbSup.Products.Add(dbProd);
                            }
                        }
                        await db.SaveChangesAsync();

                        if (_rng.NextDouble() > 0.8) // Only 20% paid
                        {
                            var po = await poParams.GetByIdAsync(poId);
                            await poParams.AddPaymentAsync(poId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
                            {
                                Amount = po!.TotalAmount,
                                PaymentMethod = PaymentMethod.BankTransfer,
                                PaymentDate = dueDate.AddDays(_rng.Next(0, 5)),
                                Reference = $"TRF-{_rng.Next(10000, 99999)}"
                            }, _user);
                        }
                    }
                }
            }

            // 4b. SALES ORDERS (Selling)
            // Query current available batches periodically
            if (_rng.NextDouble() < 0.8) // Increased from 0.5 to 0.8
            {
                var batches = await db.ProductBatches.Where(b => b.OnHand > b.Reserved).ToListAsync();
                if (batches.Any())
                {
                    var customer = customers[_rng.Next(customers.Count)];
                    var numLines = _rng.Next(1, 4);
                    var lines = new List<CreateSalesOrderLineRequest>();
                    
                    for (int i = 0; i < numLines; i++)
                    {
                        var batch = batches[_rng.Next(batches.Count)];
                        if (lines.Any(l => l.BatchNumber == batch.BatchNumber)) continue;

                        var maxQty = (int)(batch.OnHand - batch.Reserved);
                        if (maxQty <= 0) continue;

                        var qty = _rng.Next(1, Math.Min(20, maxQty + 1));
                        
                        lines.Add(new CreateSalesOrderLineRequest
                        {
                            ProductId = batch.ProductId,
                            Quantity = qty,
                            UnitPrice = batch.UnitPrice ?? 25.00m,
                            BatchNumber = batch.BatchNumber
                        });
                        
                        // Local update for this SO creation
                        batch.Reserved += qty;
                    }

                    if (lines.Any())
                    {
                        var dueDate = currentDate.AddDays(_rng.Next(3, 10));
                        var soReq = new CreateSalesOrderRequest
                        {
                            CustomerId = customer.Id,
                            OrderDate = currentDate,
                            DueDate = dueDate,
                            IsTaxInclusive = false,
                            ApplyVat = true,
                            PaymentMethod = PaymentMethod.Cash,
                            Lines = lines
                        };

                        try
                        {
                            var soId = await soParams.CreateAsync(soReq, _user);
                            soCount++;

                            if (dueDate < today)
                            {
                                 await soParams.CompleteOrderAsync(soId, _user, currentDate); // FIX: pass simulation date

                                 if (_rng.NextDouble() > 0.05) // 95% paid (increased from 90% to keep balance +ve)
                                 {
                                    var so = await soParams.GetByIdAsync(soId);
                                    await soParams.AddPaymentAsync(soId, new Inventory.Application.DTOs.Payment.CreatePaymentRequest
                                    {
                                        Amount = so!.TotalAmount,
                                        PaymentMethod = PaymentMethod.Cash,
                                        PaymentDate = currentDate
                                    }, _user);
                                 }
                            }
                        } 
                        catch (Exception ex)
                        {
                            Console.WriteLine($"SO Creation skipped: {ex.Message}");
                        }
                    }
                }
            }

            // 4c. STANDALONE TRANSACTIONS (Issue/Adjust)
            if (_rng.NextDouble() < 0.15)
            {
                var batches = await db.ProductBatches.Where(b => b.OnHand > 0).ToListAsync();
                if (batches.Any())
                {
                    var batch = batches[_rng.Next(batches.Count)];
                    var isIssue = _rng.NextDouble() > 0.3; // 70% Issue, 30% Adjust
                    
                    var qty = _rng.Next(1, 5);
                    var note = isIssue 
                        ? (new[] { "Samples", "Loss", "Expired", "Internal Use" })[_rng.Next(4)]
                        : (new[] { "Stock Correction", "Cyclical Count", "Inventory Adjustment" })[_rng.Next(3)];

                    var txReq = new CreateInventoryTransactionRequest
                    {
                        ProductId = batch.ProductId,
                        Quantity = qty,
                        Type = isIssue ? InventoryTransactionType.Issue : InventoryTransactionType.Adjust,
                        BatchNumber = batch.BatchNumber,
                        ProductBatchId = batch.Id,
                        Note = note,
                        TimestampUtc = currentDate // FIX: Pass simulation date
                    };

                    try
                    {
                        await inventoryParams.CreateAsync(txReq, _user);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Standalone Tx skipped: {ex.Message}");
                    }
                }
            }

            currentDate = currentDate.AddDays(1);
            if (_rng.NextDouble() > 0.8) currentDate = currentDate.AddDays(_rng.Next(1, 3));
        }
        
        Console.WriteLine($"Seeding complete. POs: {poCount}, SOs: {soCount}");
    }

    private static async Task CreateInitialBatch(IPurchaseOrderServices poParams, AppDbContext db, ProductResponseDto prod, SupplierResponse sup, DateTimeOffset date, Inventory.Application.DTOs.UserContext user)
    {
        var cost = 20 + (decimal)(_rng.NextDouble() * 80);
        var poReq = new CreatePurchaseOrderRequest
        {
            SupplierId = sup.Id,
            DueDate = date.AddDays(7),
            ConnectToReceiveStock = true,
            Lines = new List<CreatePurchaseOrderLineRequest>
            {
                new() { ProductId = prod.Id, Quantity = 1000, UnitPrice = Math.Round(cost, 2), BatchNumber = $"INIT-{prod.Sku}-{date:MMdd}" }
            }
        };

        var poId = await poParams.CreateAsync(poReq, user);
        await poParams.UpdateStatusAsync(poId, PurchaseOrderStatus.Received, user, date.AddDays(7)); // FIX: pass simulation date

        var batch = await db.ProductBatches.FirstOrDefaultAsync(b => b.ProductId == prod.Id && b.BatchNumber == poReq.Lines[0].BatchNumber);
        if (batch != null)
        {
            batch.UnitPrice = Math.Round(cost * 2.5m, 2); // Increased margin for initial stock
            
            // LINK SUPPLIER TO PRODUCT
            var dbSup = await db.Suppliers.Include(s => s.Products).FirstOrDefaultAsync(s => s.Id == sup.Id);
            if (dbSup != null && !dbSup.Products.Any(p => p.Id == prod.Id))
            {
                var dbProd = await db.Products.FindAsync(prod.Id);
                if (dbProd != null) dbSup.Products.Add(dbProd);
            }
            
            await db.SaveChangesAsync();
        }
    }

    private static async Task ClearDatabaseAsync(AppDbContext db)
    {
        await db.InventoryTransactions.ExecuteDeleteAsync();
        await db.FinancialTransactions.ExecuteDeleteAsync();
        await db.RefundTransactions.ExecuteDeleteAsync(); 
        await db.PaymentRecords.ExecuteDeleteAsync(); 
        await db.SalesOrderLines.ExecuteDeleteAsync();
        await db.SalesOrders.ExecuteDeleteAsync();
        await db.PurchaseOrderLines.ExecuteDeleteAsync();
        await db.PurchaseOrders.ExecuteDeleteAsync();
        await db.ProductBatches.ExecuteDeleteAsync();
        await db.StockSnapshots.ExecuteDeleteAsync(); 
        await db.Products.ExecuteDeleteAsync();
        await db.categories.ExecuteDeleteAsync();
        await db.Suppliers.ExecuteDeleteAsync();
        await db.Customers.ExecuteDeleteAsync();
        await db.SaveChangesAsync();
    }

    private static async Task<List<CategoryResponseDto>> SeedCategories(ICategoryServices svc, Inventory.Application.DTOs.UserContext user)
    {
        var names = new[] { "Acids", "Bases", "Solvents", "Salts", "Oxidizers", "Reagents", "Indicators", "Buffers" };
        var list = new List<CategoryResponseDto>();
        foreach(var n in names)
        {
            var id = await svc.CreateAsync(new CreateCategoryRequest { Name = n }, user);
            list.Add(new CategoryResponseDto { Id = id, Name = n });
        }
        return list;
    }

    private static async Task<List<SupplierResponse>> SeedSuppliers(ISupplierServices svc, Inventory.Application.DTOs.UserContext user)
    {
        var suppliers = new[] {
            ("ChemCorp International", "USA", "contact@chemcorp.com"),
            ("BioLab Solutions", "Germany", "sales@biolab.de"),
            ("Advanced Reagents Ltd", "UK", "orders@advanced-reagents.co.uk"),
            ("Pacific Chemicals", "China", "export@pacificchem.cn")
        };

        var list = new List<SupplierResponse>();
        foreach(var (name, loc, email) in suppliers)
        {
            var id = await svc.CreateAsync(new CreateSupplierRequest(name, GeneratePhone(), email, $"{_rng.Next(100,999)} Industrial Rd, {loc}"), user);
            list.Add(new SupplierResponse(id, name, null, email, null, true, DateTimeOffset.UtcNow));
        }
        return list;
    }

    private static async Task<List<CustomerResponseDto>> SeedCustomers(ICustomerServices svc, Inventory.Application.DTOs.UserContext user)
    {
        var types = new[] { "University", "Lab", "Pharma", "Manufacturing" };
        var list = new List<CustomerResponseDto>();
        
        for(int i=0; i<10; i++) // Reduced from 30 to 10
        {
            var type = types[_rng.Next(types.Length)];
            var name = $"{GenerateName()} {type}";
            var id = await svc.CreateAsync(new CreateCustomerRequest { 
                Name = name, Email = $"procurement@{name.ToLower().Replace(" ", "")}.com",
                Phone = GeneratePhone(), Address = $"{_rng.Next(1,999)} Science Park"
            }, user);
            
            list.Add(new CustomerResponseDto { Id = id, Name = name });
        }
        return list;
    }

    private static async Task<List<ProductResponseDto>> SeedProducts(IProductServices svc, List<CategoryResponseDto> cats, Inventory.Application.DTOs.UserContext user)
    {
        var products = new[] {
            ("Sodium Hydroxide", "kg"), ("Sulfuric Acid", "L"), ("Ethanol Absolute", "L"), 
            ("Acetone", "L"), ("Hydrochloric Acid", "L")
        };

        var list = new List<ProductResponseDto>();
        int idx = 1;
        foreach(var (name, unit) in products)
        {
            var cat = cats[_rng.Next(cats.Count)];
            var req = new CreateProductRequest { Sku = $"CHM-{idx:000}", Name = name, CategoryId = cat.Id, Unit = unit, ReorderPoint = 50, IsActive = true };
            var id = await svc.CreateAsync(req, user);
            list.Add(new ProductResponseDto { Id = id, Sku = req.Sku, Name = req.Name, CategoryId = cat.Id, CategoryName = cat.Name, Unit = req.Unit, IsActive = true });
            idx++;
        }
        return list;
    }

    private static string GeneratePhone() => $"+1-555-{_rng.Next(100,999)}-{_rng.Next(1000,9999)}";
    private static string GenerateName() 
    {
        var prefixes = new[] { "Alpha", "Beta", "Omega", "Apex", "Prime" };
        var suffixes = new[] { "Systems", "Technologies", "Dynamics", "Labs" };
        return $"{prefixes[_rng.Next(prefixes.Length)]} {suffixes[_rng.Next(suffixes.Length)]}";
    }
}
