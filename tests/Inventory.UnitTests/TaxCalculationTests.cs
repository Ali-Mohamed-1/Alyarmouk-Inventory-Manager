    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Inventory.Application.DTOs;
    using Inventory.Application.DTOs.PurchaseOrder;
    using Inventory.Application.DTOs.SalesOrder;
    using Inventory.Application.DTOs.StockSnapshot;
    using Inventory.Application.DTOs.Transaction;
    using Inventory.Domain.Entities;
    using Inventory.Infrastructure.Data;
    using Inventory.Infrastructure.Services;
    using Inventory.Application.Abstractions;
    using Microsoft.EntityFrameworkCore;
    using Xunit;

    namespace Inventory.UnitTests
    {
        public class TaxCalculationTests : IDisposable
        {
            private readonly AppDbContext _db;
            private readonly SalesOrderServices _salesServices;
            private readonly PurchaseOrderServices _purchaseServices;
            private readonly UserContext _user;

            public TaxCalculationTests()
            {
                var options = new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                    .ConfigureWarnings(x => x.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                    .Options;

                _db = new AppDbContext(options);
                _db.Database.EnsureCreated();

                var invServicesMock = new MockInventoryServices();
                var finServicesMock = new MockFinancialServices();

                _salesServices = new SalesOrderServices(_db, invServicesMock, finServicesMock);
                _purchaseServices = new PurchaseOrderServices(_db, invServicesMock, finServicesMock);

                _user = new UserContext("test-user", "Test User");

                SeedData();
            }

            private void SeedData()
            {
                _db.Customers.Add(new Customer { Id = 1, Name = "Test Customer" });
                _db.Suppliers.Add(new Supplier { Id = 1, Name = "Test Supplier", IsActive = true });
                _db.Products.Add(new Product { Id = 1, Name = "Product A", Unit = "PCS", IsActive = true });
                _db.StockSnapshots.Add(new StockSnapshot { ProductId = 1, OnHand = 1000, Reserved = 0 });
                _db.SaveChanges();
            }

            public void Dispose()
            {
                _db.Database.EnsureDeleted();
                _db.Dispose();
            }

            [Fact]
            public async Task CreateSalesOrder_TaxInclusive_VatOnly_CalculatesCorrectly()
            {
                // 100 EGP Item, 14% VAT only -> Base: 87.72, Tax: 12.28
                var req = new CreateSalesOrderRequest
                {
                    CustomerId = 1,
                    DueDate = DateTimeOffset.UtcNow.AddDays(7),
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Pending,
                    IsTaxInclusive = true,
                    ApplyVat = true,
                    ApplyManufacturingTax = false,
                    Lines = new List<CreateSalesOrderLineRequest>
                    {
                        new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                    }
                };

                await _salesServices.CreateAsync(req, _user);
                var order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync();

                Assert.NotNull(order);
                Assert.Equal(87.72m, order.Subtotal); // 100 / 1.14
                Assert.Equal(12.28m, order.VatAmount);
                Assert.Equal(0m, order.ManufacturingTaxAmount);
                Assert.Equal(100m, order.TotalAmount);
            }

            [Fact]
            public async Task CreateSalesOrder_TaxExclusive_VatOnly_CalculatesCorrectly()
            {
                // 100 EGP Item, 14% VAT only -> Base: 100, Tax: 14, Total: 114
                var req = new CreateSalesOrderRequest
                {
                    CustomerId = 1,
                    DueDate = DateTimeOffset.UtcNow.AddDays(7),
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Pending,
                    IsTaxInclusive = false,
                    ApplyVat = true,
                    ApplyManufacturingTax = false,
                    Lines = new List<CreateSalesOrderLineRequest>
                    {
                        new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                    }
                };

                await _salesServices.CreateAsync(req, _user);
                var order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync();

                Assert.NotNull(order);
                Assert.Equal(100m, order.Subtotal);
                Assert.Equal(14m, order.VatAmount);
                Assert.Equal(0m, order.ManufacturingTaxAmount);
                Assert.Equal(114m, order.TotalAmount);
            }

            [Fact]
            public async Task CreateSalesOrder_TaxInclusive_VatAndMan_CalculatesCorrectly()
            {
                // Price = 100.
                // Formula: Base = Price / (1 + VatRate - ManTaxRate) = 100 / (1 + 0.14 - 0.01) = 100 / 1.13 = 88.50
                // VAT = Round(88.50 * 0.14) = 12.39
                // ManTax = Round(88.50 * 0.01) = 0.89
                // Total = 88.50 + 12.39 - 0.89 = 100
                var req = new CreateSalesOrderRequest
                {
                    CustomerId = 1,
                    DueDate = DateTimeOffset.UtcNow.AddDays(7),
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Pending,
                    IsTaxInclusive = true,
                    ApplyVat = true,
                    ApplyManufacturingTax = true,
                    Lines = new List<CreateSalesOrderLineRequest>
                    {
                        new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                    }
                };

                await _salesServices.CreateAsync(req, _user);
                var order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync();

                Assert.NotNull(order);
                Assert.Equal(88.50m, order.Subtotal);
                Assert.Equal(12.39m, order.VatAmount);
                Assert.Equal(0.89m, order.ManufacturingTaxAmount);
                Assert.Equal(100m, order.TotalAmount);
            }

            [Fact]
            public async Task CreatePurchaseOrder_TaxExclusive_VatAndMan_CalculatesCorrectly()
            {
                // 100 Base -> VAT: 14, ManTax: 1.
                // Total = 100 + 14 - 1 + 10 = 123.
                var req = new CreatePurchaseOrderRequest
                {
                    SupplierId = 1,
                    IsTaxInclusive = false,
                    ApplyVat = true,
                    ApplyManufacturingTax = true,
                    ReceiptExpenses = 10,
                    Lines = new List<CreatePurchaseOrderLineRequest>
                    {
                        new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                    }
                };

                await _purchaseServices.CreateAsync(req, _user);
                var order = await _db.PurchaseOrders.Include(o => o.Lines).FirstOrDefaultAsync();

                Assert.NotNull(order);
                Assert.Equal(100m, order.Subtotal);
                Assert.Equal(14m, order.VatAmount);
                Assert.Equal(1m, order.ManufacturingTaxAmount);
                Assert.Equal(123m, order.TotalAmount);
            }

            [Fact]
            public async Task CreateSalesOrder_TaxInclusive_ManOnly_CalculatesCorrectly()
            {
                 // 100 Total -> Base: 100 / (1 - 0.01) = 100 / 0.99 = 101.01. 
                 // Man Tax = Round(101.01 * 0.01) = 1.01.
                 // Total = 101.01 - 1.01 = 100.
                var req = new CreateSalesOrderRequest
                {
                    CustomerId = 1,
                    DueDate = DateTimeOffset.UtcNow.AddDays(7),
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Pending,
                    IsTaxInclusive = true,
                    ApplyVat = false,
                    ApplyManufacturingTax = true,
                    Lines = new List<CreateSalesOrderLineRequest>
                    {
                        new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                    }
                };

                await _salesServices.CreateAsync(req, _user);
                var order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync();

                Assert.NotNull(order);
                Assert.Equal(101.01m, order.Subtotal);
                Assert.Equal(0m, order.VatAmount);
                Assert.Equal(1.01m, order.ManufacturingTaxAmount);
                Assert.Equal(100m, order.TotalAmount);
            }

            [Fact]
            public async Task CreateSalesOrder_TaxExclusive_ManOnly_CalculatesCorrectly()
            {
                 // 100 Base -> Man Tax = 1.00.
                 // Total = 100 - 1 = 99.
                var req = new CreateSalesOrderRequest
                {
                    CustomerId = 1,
                    DueDate = DateTimeOffset.UtcNow.AddDays(7),
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Pending,
                    IsTaxInclusive = false,
                    ApplyVat = false,
                    ApplyManufacturingTax = true,
                    Lines = new List<CreateSalesOrderLineRequest>
                    {
                        new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                    }
                };

                await _salesServices.CreateAsync(req, _user);
                var order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync();

                Assert.NotNull(order);
                Assert.Equal(100m, order.Subtotal);
                Assert.Equal(0m, order.VatAmount);
                Assert.Equal(1m, order.ManufacturingTaxAmount);
                Assert.Equal(99m, order.TotalAmount);
            }

            [Fact]
            public async Task CreateSalesOrder_NoTaxes_CalculatesCorrectly()
            {
                // 100 Base -> Total 100.
                var req = new CreateSalesOrderRequest
                {
                    CustomerId = 1,
                    DueDate = DateTimeOffset.UtcNow.AddDays(7),
                    PaymentMethod = PaymentMethod.Cash,
                    PaymentStatus = PaymentStatus.Pending,
                    IsTaxInclusive = false,
                    ApplyVat = false,
                    ApplyManufacturingTax = false,
                    Lines = new List<CreateSalesOrderLineRequest>
                    {
                        new() { ProductId = 1, Quantity = 1, UnitPrice = 100 }
                    }
                };

                await _salesServices.CreateAsync(req, _user);
                var order = await _db.SalesOrders.Include(o => o.Lines).FirstOrDefaultAsync();

                Assert.NotNull(order);
                Assert.Equal(100m, order.Subtotal);
                Assert.Equal(0m, order.VatAmount);
                Assert.Equal(0m, order.ManufacturingTaxAmount);
                Assert.Equal(100m, order.TotalAmount);
            }

        [Fact]
        public async Task CreateSalesOrder_MultipleLines_SumsCorrectlyWithoutRoundingErrors()
        {
            // 3 lines of 33.33 EGP (Tax Inclusive)
            // Total should be 99.99 exactly.
            var req = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                IsTaxInclusive = true,
                ApplyVat = true,
                ApplyManufacturingTax = true,
                Lines = new List<CreateSalesOrderLineRequest>
        {
            new() { ProductId = 1, Quantity = 1, UnitPrice = 33.33m },
            new() { ProductId = 1, Quantity = 1, UnitPrice = 33.33m },
            new() { ProductId = 1, Quantity = 1, UnitPrice = 33.33m }
        }
            };

            await _salesServices.CreateAsync(req, _user);
            var order = await _db.SalesOrders.FirstOrDefaultAsync();

            Assert.Equal(99.99m, order.TotalAmount);
            // Ensure Subtotal + Vat - ManTax = Total exactly
            Assert.Equal(order.TotalAmount, order.Subtotal + order.VatAmount - order.ManufacturingTaxAmount);
        }

        [Fact]
        public async Task CreatePurchaseOrder_InvalidSupplier_ThrowsNotFoundException()
        {
            var req = new CreatePurchaseOrderRequest
            {
                SupplierId = 9999, // Non-existent
                Lines = new List<CreatePurchaseOrderLineRequest> { new() { ProductId = 1, Quantity = 1, UnitPrice = 100 } }
            };

            await Assert.ThrowsAsync<Inventory.Infrastructure.Services.NotFoundException>(() => _purchaseServices.CreateAsync(req, _user));
        }

        [Fact]
        public async Task CreateSalesOrder_ZeroQuantity_ThrowsValidationException()
        {
            var req = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                Lines = new List<CreateSalesOrderLineRequest> { new() { ProductId = 1, Quantity = 0, UnitPrice = 100 } }
            };

            await Assert.ThrowsAsync<Inventory.Infrastructure.Services.ValidationException>(() => _salesServices.CreateAsync(req, _user));
        }

        [Fact]
        public async Task CreateSalesOrder_ZeroPrice_CalculatesZeroTax()
        {
            // Free sample: 0 Price -> 0 Tax
            var req = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                IsTaxInclusive = true,
                ApplyVat = true,
                ApplyManufacturingTax = true,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 1, UnitPrice = 0 }
                }
            };

            await _salesServices.CreateAsync(req, _user);
            var order = await _db.SalesOrders.FirstOrDefaultAsync();

            Assert.Equal(0m, order.Subtotal);
            Assert.Equal(0m, order.VatAmount);
            Assert.Equal(0m, order.ManufacturingTaxAmount);
            Assert.Equal(0m, order.TotalAmount);
        }

        [Fact]
        public async Task CreateSalesOrder_Rounding_Check()
        {
            // 10 items at 1.11 inclusive (Total 11.10)
            // 1.11 / 1.15 = 0.9652... -> 0.97 Base? 
            // Let's check logic:
            // Base = Round(1.11 / 1.15, 2) = Round(0.9652) = 0.97
            // Vat = Round(0.97 * 0.14) = Round(0.1358) = 0.14
            // Man = Round(0.97 * 0.01) = Round(0.0097) = 0.01
            // Total Line = 0.97 + 0.14 + 0.01 = 1.12.
            // ERROR: 1.12 != 1.11.
            // My implementation does "LineTotal = lineSubtotal + lineVat + lineManTax;"
            // It assumes the sum matches the inclusive price. But independent rounding usually breaks this.
            // Spec said: "if this order is tax included : you add -14%... to the total"
            // Actually implementation:
            // baseAmount = Math.Round(baseAmount, 2);
            // ...
            // lineTotal = lineSubtotal + lineVat + lineManTax;
            // The LineTotal is recalculated from components. If I want it to MATCH inclusive EXACTLY, I might have a cent diff.
            // User requirement: "ensure that its compatable with the provided taxing system"
            // The provided "Scenario" text:
            // "if this order is tax included : you add -14% ... of the total order to the total 14% tax"
            // This implies extracting tax from total.
            
            // Let's verify what happens in this edge case with current code.
            
            var req = new CreateSalesOrderRequest
            {
                CustomerId = 1,
                DueDate = DateTimeOffset.UtcNow.AddDays(7),
                PaymentMethod = PaymentMethod.Cash,
                PaymentStatus = PaymentStatus.Pending,
                IsTaxInclusive = true,
                ApplyVat = true,
                ApplyManufacturingTax = true,
                Lines = new List<CreateSalesOrderLineRequest>
                {
                    new() { ProductId = 1, Quantity = 10, UnitPrice = 1.11m }
                }
            };
            
            // Total Requested: 11.10
            
            await _salesServices.CreateAsync(req, _user);
            var order = await _db.SalesOrders.FirstOrDefaultAsync();
            
            // Current Logic:
            // Line (qty 10): 
            // TotalLinePrice = 11.10
            // Base = 11.10 / 1.13 = 9.823... -> 9.82
            // Vat = 9.82 * 0.14 = 1.3748 -> 1.37
            // Man = 9.82 * 0.01 = 0.0982 -> 0.10
            // Total = 9.82 + 1.37 - 0.10 = 11.09.
            // Difference of 0.01 due to rounding.
            
            Assert.Equal(11.10m, order.TotalAmount);
        }


        public class MockInventoryTransactionServices : IInventoryTransactionServices
        {
            public Task<long> CreateAsync(CreateInventoryTransactionRequest req, UserContext user, CancellationToken ct = default) => Task.FromResult(0L);
            public Task<IReadOnlyList<InventoryTransactionResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<InventoryTransactionResponseDto>)new List<InventoryTransactionResponseDto>());
            public Task<IReadOnlyList<InventoryTransactionResponseDto>> GetByProductAsync(int productId, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<InventoryTransactionResponseDto>)new List<InventoryTransactionResponseDto>());
            public Task<IReadOnlyList<InventoryTransactionResponseDto>> GetTransactionsByCustomerAsync(int customerId, int take = 100, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<InventoryTransactionResponseDto>)new List<InventoryTransactionResponseDto>());
        }

        public class MockInventoryServices : IInventoryServices
        {
            public Task<decimal> GetOnHandAsync(int productId, CancellationToken ct = default) => Task.FromResult(1000m);
            public Task<StockSnapshotResponseDto?> GetStockAsync(int productId, CancellationToken ct = default) => Task.FromResult<StockSnapshotResponseDto?>(null);
            public Task<IReadOnlyList<StockSnapshotResponseDto>> GetAllStockAsync(CancellationToken ct = default) => Task.FromResult((IReadOnlyList<StockSnapshotResponseDto>)new List<StockSnapshotResponseDto>());
            public Task ReceiveAsync(StockReceiveRequest req, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task IssueAsync(StockIssueRequest req, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task UpdateStockAsync(UpdateStockRequest req, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task<long> CreateTransactionAsync(CreateInventoryTransactionRequest req, UserContext user, CancellationToken ct = default) => Task.FromResult(0L);
            public Task<IReadOnlyList<InventoryTransactionResponseDto>> GetRecentTransactionsAsync(int take = 50, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<InventoryTransactionResponseDto>)new List<InventoryTransactionResponseDto>());
            public Task<IReadOnlyList<InventoryTransactionResponseDto>> GetProductTransactionsAsync(int productId, CancellationToken ct = default) => Task.FromResult((IReadOnlyList<InventoryTransactionResponseDto>)new List<InventoryTransactionResponseDto>());
            public Task ProcessPurchaseOrderStockAsync(long purchaseOrderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default) => Task.CompletedTask;
            public Task ReversePurchaseOrderStockAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task ProcessSalesOrderStockAsync(long salesOrderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default) => Task.CompletedTask;
            public Task ReverseSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task RefundSalesOrderStockAsync(long salesOrderId, List<RefundLineItem> lines, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task RefundPurchaseOrderStockAsync(long purchaseOrderId, List<RefundPurchaseLineItem> lines, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task ReserveSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
            public Task ReleaseSalesOrderReservationAsync(long salesOrderId, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        }

        public class MockFinancialServices : IFinancialServices
        {
            public Task CreateFinancialTransactionFromPaymentAsync(PaymentRecord payment, UserContext user, CancellationToken ct = default) => Task.CompletedTask;
        }
    }
}
