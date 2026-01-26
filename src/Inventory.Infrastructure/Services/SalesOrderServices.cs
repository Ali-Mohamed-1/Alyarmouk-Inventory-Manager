using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Domain.Constants;

namespace Inventory.Infrastructure.Services
{
    public sealed class SalesOrderServices : ISalesOrderServices
    {

        private const int MaxTake = 1000;
        private readonly AppDbContext _db;
        private readonly IAuditLogWriter _auditWriter;

        public SalesOrderServices(
            AppDbContext db,
            IAuditLogWriter auditWriter)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        }

        public async Task<long> CreateAsync(CreateSalesOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.CustomerId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Customer ID must be positive.");

            if (req.Lines is null || req.Lines.Count == 0)
                throw new ValidationException("Sales order must have at least one line item.");

            // Verify customer exists
            var customer = await _db.Customers
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == req.CustomerId, ct);

            if (customer is null)
                throw new NotFoundException($"Customer id {req.CustomerId} was not found.");

            // Validate and group line items by product (combine quantities if duplicate products)
            // Store both quantity and unit price (use first price if product appears multiple times)
            var lineItems = new Dictionary<int, (decimal Quantity, decimal UnitPrice)>();
            var productIds = new HashSet<int>();

            foreach (var line in req.Lines)
            {
                if (line.ProductId <= 0)
                    throw new ValidationException("Product ID must be positive for all line items.");

                if (line.Quantity <= 0)
                    throw new ValidationException("Quantity must be greater than zero for all line items.");

                if (line.UnitPrice < 0)
                    throw new ValidationException("Unit price cannot be negative.");

                if (lineItems.ContainsKey(line.ProductId))
                {
                    var existing = lineItems[line.ProductId];
                    lineItems[line.ProductId] = (existing.Quantity + line.Quantity, existing.UnitPrice);
                }
                else
                {
                    lineItems[line.ProductId] = (line.Quantity, line.UnitPrice);
                    productIds.Add(line.ProductId);
                }
            }

            // Load all products at once for validation
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);

            if (products.Count != productIds.Count)
            {
                var missingIds = productIds.Except(products.Select(p => p.Id)).ToList();
                throw new NotFoundException($"Product(s) not found: {string.Join(", ", missingIds)}");
            }

            // Verify all products are active
            var inactiveProducts = products.Where(p => !p.IsActive).ToList();
            if (inactiveProducts.Any())
            {
                throw new ValidationException($"Cannot create order with inactive product(s): {string.Join(", ", inactiveProducts.Select(p => p.Name))}");
            }

            // Load stock snapshots for all products
            var productIdsList = productIds.ToList();
            var stockSnapshots = await _db.StockSnapshots
                .Where(s => productIdsList.Contains(s.ProductId))
                .ToListAsync(ct);

            // Verify sufficient stock for all products
            foreach (var kvp in lineItems)
            {
                var productId = kvp.Key;
                var quantity = kvp.Value.Quantity;

                var snapshot = stockSnapshots.FirstOrDefault(s => s.ProductId == productId);
                var availableStock = snapshot?.OnHand ?? 0;

                if (availableStock < quantity)
                {
                    var product = products.First(p => p.Id == productId);
                    throw new ValidationException($"Insufficient stock for product '{product.Name}'. Available: {availableStock}, Requested: {quantity}");
                }
            }

            // Generate unique order number
            var orderNumber = await GenerateUniqueOrderNumberAsync(ct);

            // Use transaction to ensure all operations are atomic
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Create sales order
                var salesOrder = new SalesOrder
                {
                    OrderNumber = orderNumber,
                    CustomerId = req.CustomerId,
                    CustomerNameSnapshot = customer.Name,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = user.UserId,
                    CreatedByUserDisplayName = user.UserDisplayName,
                    Note = req.Note,
                    IsTaxInclusive = req.IsTaxInclusive,
                    ApplyVat = req.ApplyVat,
                    ApplyManufacturingTax = req.ApplyManufacturingTax
                };

                _db.SalesOrders.Add(salesOrder);
                await _db.SaveChangesAsync(ct); // Save to get the ID

                var inventoryTransactions = new List<InventoryTransaction>();

                decimal totalVat = 0;
                decimal totalManTax = 0;
                decimal totalSubtotal = 0;
                decimal totalOrderAmount = 0;

                // Create order lines and update stock
                foreach (var kvp in lineItems)
                {
                    var productId = kvp.Key;
                    var (quantity, unitPrice) = kvp.Value;
                    var product = products.First(p => p.Id == productId);

                    // SIMPLIFIED TAX CALCULATION
                    // Base is always unitPrice * quantity
                    decimal baseAmount = unitPrice * quantity;
                    decimal lineSubtotal = baseAmount;
                    decimal lineVat = 0;
                    decimal lineManTax = 0;
                    decimal lineTotal;

                    // Calculate VAT if enabled
                    if (req.ApplyVat)
                    {
                        lineVat = Math.Round(baseAmount * TaxConstants.VatRate, 2, MidpointRounding.AwayFromZero);
                    }

                    // Calculate Manufacturing Tax if enabled
                    if (req.ApplyManufacturingTax)
                    {
                        lineManTax = Math.Round(baseAmount * TaxConstants.ManufacturingTaxRate, 2, MidpointRounding.AwayFromZero);
                    }

                    // Total = Base + VAT - ManTax
                    lineTotal = lineSubtotal + lineVat - lineManTax;

                    totalSubtotal += lineSubtotal;
                    totalVat += lineVat;
                    totalManTax += lineManTax;
                    totalOrderAmount += lineTotal;

                    // Create order line
                    var orderLine = new SalesOrderLine
                    {
                        SalesOrderId = salesOrder.Id,
                        ProductId = productId,
                        ProductNameSnapshot = product.Name,
                        UnitSnapshot = product.Unit,
                        Quantity = quantity,
                        UnitPrice = unitPrice,
                        IsTaxInclusive = req.IsTaxInclusive, // Keep for reference, but doesn't affect calculation
                        LineSubtotal = lineSubtotal,
                        LineVatAmount = lineVat,
                        LineManufacturingTaxAmount = lineManTax,
                        LineTotal = lineTotal
                    };

                    _db.SalesOrderLines.Add(orderLine);

                    // Update stock snapshot
                    var snapshot = stockSnapshots.FirstOrDefault(s => s.ProductId == productId);
                    if (snapshot is null)
                    {
                        snapshot = new StockSnapshot
                        {
                            ProductId = productId,
                            OnHand = 0
                        };
                        _db.StockSnapshots.Add(snapshot);
                    }

                    snapshot.OnHand -= quantity; // Decrease stock for sale

                    // Create inventory transaction (Issue) with cost tracking
                    var inventoryTransaction = new InventoryTransaction
                    {
                        ProductId = productId,
                        QuantityDelta = -quantity, // Negative for Issue
                        UnitCost = product.Cost, // Cost for COGS calculation
                        Type = InventoryTransactionType.Issue,
                        TimestampUtc = DateTimeOffset.UtcNow,
                        UserId = user.UserId,
                        UserDisplayName = user.UserDisplayName,
                        clientId = req.CustomerId,
                        Note = $"Sales order {orderNumber}"
                    };

                    _db.InventoryTransactions.Add(inventoryTransaction);
                    inventoryTransactions.Add(inventoryTransaction);
                }

                await _db.SaveChangesAsync(ct); // Save to get inventory transaction IDs

                // Create financial transactions for COGS (Cost of Goods Sold) for each line
                for (int i = 0; i < inventoryTransactions.Count; i++)
                {
                    var inventoryTransaction = inventoryTransactions[i];
                    var productId = inventoryTransaction.ProductId;
                    var product = products.First(p => p.Id == productId);
                    var quantity = lineItems[productId].Quantity;
                    var cogsAmount = product.Cost * quantity;

                    var cogsTransaction = new FinancialTransaction
                    {
                        Type = FinancialTransactionType.Expense, // Money going out (COGS)
                        Amount = cogsAmount,
                        InventoryTransactionId = inventoryTransaction.Id,
                        SalesOrderId = salesOrder.Id,
                        ProductId = productId,
                        CustomerId = req.CustomerId,
                        TimestampUtc = DateTimeOffset.UtcNow,
                        UserId = user.UserId,
                        UserDisplayName = user.UserDisplayName,
                        Note = $"COGS for sales order {orderNumber} - {product.Name}"
                    };

                    _db.FinancialTransactions.Add(cogsTransaction);
                }

                salesOrder.Subtotal = totalSubtotal;
                salesOrder.VatAmount = totalVat;
                salesOrder.ManufacturingTaxAmount = totalManTax;
                salesOrder.TotalAmount = totalOrderAmount;

                await _db.SaveChangesAsync(ct);

                // AUDIT LOG: Record the order creation
                await _auditWriter.LogCreateAsync<SalesOrder>(
                    salesOrder.Id,
                    user,
                    afterState: new
                    {
                        OrderNumber = salesOrder.OrderNumber,
                        CustomerId = salesOrder.CustomerId,
                        CustomerName = salesOrder.CustomerNameSnapshot,
                        LineCount = lineItems.Count,
                        Note = salesOrder.Note,
                        TotalAmount = salesOrder.TotalAmount
                    },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return salesOrder.Id;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not create sales order due to a database conflict.", ex);
            }
        }

        public async Task<SalesOrderResponseDto?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Where(o => o.Id == id)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    Status = o.Status,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        ProductName = l.ProductNameSnapshot,
                        Quantity = l.Quantity,
                        Unit = l.UnitSnapshot,
                        UnitPrice = l.UnitPrice
                    }).ToList()
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<SalesOrderResponseDto>> GetCustomerOrdersAsync(int customerId, int take = 100, CancellationToken ct = default)
        {
            if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId), "Customer ID must be positive.");
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Where(o => o.CustomerId == customerId)
                .OrderByDescending(o => o.CreatedUtc)
                .Take(take)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    Status = o.Status,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        ProductName = l.ProductNameSnapshot,
                        Quantity = l.Quantity,
                        Unit = l.UnitSnapshot,
                        UnitPrice = l.UnitPrice
                    }).ToList()
                })
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<SalesOrderResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .OrderByDescending(o => o.CreatedUtc)
                .Take(take)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    Status = o.Status,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        ProductName = l.ProductNameSnapshot,
                        Quantity = l.Quantity,
                        Unit = l.UnitSnapshot,
                        UnitPrice = l.UnitPrice
                    }).ToList()
                })
                .ToListAsync(ct);
        }

        public async Task CompleteOrderAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            // Load the order with lines
            var salesOrder = await _db.SalesOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (salesOrder.Status == SalesOrderStatus.Completed)
                throw new ValidationException($"Sales order {orderId} is already completed.");

            if (salesOrder.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException($"Cannot complete a cancelled sales order {orderId}.");

            // Use transaction to ensure all operations are atomic
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                decimal totalRevenue = 0;

                // Create financial transactions for revenue (money coming in)
                foreach (var line in salesOrder.Lines)
                {
                    var lineRevenue = line.UnitPrice * line.Quantity;
                    totalRevenue += lineRevenue;

                    var revenueTransaction = new FinancialTransaction
                    {
                        Type = FinancialTransactionType.Revenue, // Money coming in
                        Amount = lineRevenue,
                        SalesOrderId = salesOrder.Id,
                        ProductId = line.ProductId,
                        CustomerId = salesOrder.CustomerId,
                        TimestampUtc = DateTimeOffset.UtcNow,
                        UserId = user.UserId,
                        UserDisplayName = user.UserDisplayName,
                        Note = $"Revenue from sales order {salesOrder.OrderNumber} - {line.ProductNameSnapshot}"
                    };

                    _db.FinancialTransactions.Add(revenueTransaction);
                }

                // Update order status to Completed
                var previousStatus = salesOrder.Status;
                salesOrder.Status = SalesOrderStatus.Completed;

                await _db.SaveChangesAsync(ct);

                // AUDIT LOG: Record the order completion
                await _auditWriter.LogUpdateAsync<SalesOrder>(
                    salesOrder.Id,
                    user,
                    beforeState: new { Status = previousStatus.ToString() },
                    afterState: new
                    {
                        Status = SalesOrderStatus.Completed.ToString(),
                        TotalRevenue = totalRevenue
                    },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not complete order due to a database conflict.", ex);
            }
        }

        public async Task UpdateOrderStatusAsync(long orderId, UpdateSalesOrderStatusRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");
            if (req is null) throw new ArgumentNullException(nameof(req));

            // Load the order
            var salesOrder = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            // Validate status transition
            if (salesOrder.Status == req.Status)
                throw new ValidationException($"Sales order {orderId} is already in status {req.Status}.");

            // Use transaction to ensure all operations are atomic
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousStatus = salesOrder.Status;
                salesOrder.Status = req.Status;

                await _db.SaveChangesAsync(ct);

                // AUDIT LOG: Record the status update
                await _auditWriter.LogUpdateAsync<SalesOrder>(
                    salesOrder.Id,
                    user,
                    beforeState: new { Status = previousStatus.ToString() },
                    afterState: new { Status = req.Status.ToString() },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update order status due to a database conflict.", ex);
            }
        }

        #region Helper Methods

        private static void ValidateUser(UserContext user)
        {
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (string.IsNullOrWhiteSpace(user.UserId)) throw new UnauthorizedAccessException("Missing user id.");
            if (string.IsNullOrWhiteSpace(user.UserDisplayName)) throw new UnauthorizedAccessException("Missing user display name.");
        }

        private async Task<string> GenerateUniqueOrderNumberAsync(CancellationToken ct)
        {
            // Generate order number: SO-YYYYMMDD-HHMMSS-Random
            var timestamp = DateTimeOffset.UtcNow;
            var datePart = timestamp.ToString("yyyyMMdd");
            var timePart = timestamp.ToString("HHmmss");
            var randomPart = new Random().Next(100, 999).ToString();

            var baseOrderNumber = $"SO-{datePart}-{timePart}-{randomPart}";
            var orderNumber = baseOrderNumber;

            // Ensure uniqueness (retry if collision)
            int attempts = 0;
            while (await _db.SalesOrders.AnyAsync(o => o.OrderNumber == orderNumber, ct) && attempts < 10)
            {
                randomPart = new Random().Next(100, 999).ToString();
                orderNumber = $"SO-{datePart}-{timePart}-{randomPart}";
                attempts++;
            }

            if (attempts >= 10)
                throw new ConflictException("Could not generate unique order number. Please try again.");

            return orderNumber;
        }

        #endregion
    }
}
