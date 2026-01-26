using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Constants;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Infrastructure.Services
{
    public sealed class PurchaseOrderServices : IPurchaseOrderServices
    {
        private readonly AppDbContext _db;
        private readonly IAuditLogWriter _auditWriter;

        public PurchaseOrderServices(
            AppDbContext db,
            IAuditLogWriter auditWriter)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
        }

        public async Task<long> CreateAsync(CreatePurchaseOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (req.SupplierId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Supplier ID must be positive.");

            if (req.Lines is null || req.Lines.Count == 0)
                throw new ValidationException("Purchase order must have at least one line item.");

            // Verify supplier exists
            var supplier = await _db.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == req.SupplierId, ct);

            if (supplier is null)
                throw new NotFoundException($"Supplier id {req.SupplierId} was not found.");

            // Validate and group line items by product
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
                    // Combine quantities, keep first price
                    var existing = lineItems[line.ProductId];
                    lineItems[line.ProductId] = (existing.Quantity + line.Quantity, existing.UnitPrice);
                }
                else
                {
                    lineItems[line.ProductId] = (line.Quantity, line.UnitPrice);
                    productIds.Add(line.ProductId);
                }
            }

            // Load products (need tracking for cost updates)
            var products = await _db.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);

            if (products.Count != productIds.Count)
            {
                var missingIds = productIds.Except(products.Select(p => p.Id)).ToList();
                throw new NotFoundException($"Product(s) not found: {string.Join(", ", missingIds)}");
            }

            // Generate unique order number
            var orderNumber = await GenerateUniqueOrderNumberAsync(ct);

            // Use transaction
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var purchaseOrder = new PurchaseOrder
                {
                    OrderNumber = orderNumber,
                    SupplierId = req.SupplierId,
                    SupplierNameSnapshot = supplier.Name,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    CreatedByUserId = user.UserId,
                    CreatedByUserDisplayName = user.UserDisplayName,
                    Note = req.Note,
                    IsTaxInclusive = req.IsTaxInclusive,
                    ApplyVat = req.ApplyVat,
                    ApplyManufacturingTax = req.ApplyManufacturingTax,
                    ReceiptExpenses = req.ReceiptExpenses
                };

                // Add to DB context to generate ID
                _db.PurchaseOrders.Add(purchaseOrder);
                await _db.SaveChangesAsync(ct);

                decimal totalVat = 0;
                decimal totalManTax = 0;
                decimal totalSubtotal = 0;
                decimal totalOrderAmount = 0;

                foreach (var kvp in lineItems)
                {
                    var productId = kvp.Key;
                    var quantity = kvp.Value.Quantity;
                    var unitPrice = kvp.Value.UnitPrice;
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

                    // Calculate Manufacturing Tax if enabled (1%)
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

                    var poLine = new PurchaseOrderLine
                    {
                        PurchaseOrderId = purchaseOrder.Id,
                        ProductId = productId,
                        ProductNameSnapshot = product.Name,
                        Quantity = quantity,
                        UnitSnapshot = product.Unit,
                        UnitPrice = unitPrice,
                        IsTaxInclusive = req.IsTaxInclusive,
                        LineSubtotal = lineSubtotal,
                        LineVatAmount = lineVat,
                        LineManufacturingTaxAmount = lineManTax,
                        LineTotal = lineTotal
                    };

                    _db.PurchaseOrderLines.Add(poLine);

                    if (req.ConnectToReceiveStock)
                    {
                        // ============================================================
                        // UPDATE PRODUCT COST using WEIGHTED AVERAGE
                        // ============================================================

                        var snapshot = await _db.StockSnapshots
                            .FirstOrDefaultAsync(s => s.ProductId == productId, ct);

                        decimal oldCost = product.Cost;
                        decimal newCost;

                        if (snapshot is null || snapshot.OnHand == 0)
                        {
                            // First purchase OR zero stock - use purchase price as cost
                            newCost = unitPrice;

                            // Create snapshot if doesn't exist
                            if (snapshot is null)
                            {
                                snapshot = new StockSnapshot
                                {
                                    ProductId = productId,
                                    OnHand = 0,
                                    Reserved = 0
                                };
                                _db.StockSnapshots.Add(snapshot);
                            }
                        }
                        else
                        {
                            // Weighted Average Cost calculation
                            // New Cost = (Old Stock Value + New Purchase Value) / (Old Stock + New Stock)
                            decimal oldStockValue = snapshot.OnHand * product.Cost;
                            decimal newPurchaseValue = quantity * unitPrice;
                            decimal totalValue = oldStockValue + newPurchaseValue;
                            decimal totalQuantity = snapshot.OnHand + quantity;

                            newCost = Math.Round(totalValue / totalQuantity, 2, MidpointRounding.AwayFromZero);
                        }

                        // Update product cost
                        product.Cost = newCost;
                        _db.Products.Update(product);

                        // Update stock quantity
                        snapshot.OnHand += quantity;

                        // Create Inventory Transaction (Receipt)
                        var inventoryTransaction = new InventoryTransaction
                        {
                            ProductId = productId,
                            QuantityDelta = quantity,
                            UnitCost = newCost, // Use the newly calculated cost
                            Type = InventoryTransactionType.Receive,
                            TimestampUtc = DateTimeOffset.UtcNow,
                            UserId = user.UserId,
                            UserDisplayName = user.UserDisplayName,
                            clientId = req.SupplierId,
                            Note = $"Purchase Order {orderNumber}"
                        };
                        _db.InventoryTransactions.Add(inventoryTransaction);

                        // Log cost change in audit if cost changed
                        if (oldCost != newCost)
                        {
                            await _auditWriter.LogUpdateAsync<Product>(
                                product.Id,
                                user,
                                beforeState: new { Cost = oldCost },
                                afterState: new { Cost = newCost },
                                ct);
                        }
                    }
                }

                purchaseOrder.Subtotal = totalSubtotal;
                purchaseOrder.VatAmount = totalVat;
                purchaseOrder.ManufacturingTaxAmount = totalManTax;
                purchaseOrder.TotalAmount = totalOrderAmount + req.ReceiptExpenses;

                await _db.SaveChangesAsync(ct);

                // AUDIT LOG
                await _auditWriter.LogCreateAsync<PurchaseOrder>(
                    purchaseOrder.Id,
                    user,
                    afterState: new
                    {
                        OrderNumber = purchaseOrder.OrderNumber,
                        SupplierId = purchaseOrder.SupplierId,
                        LineCount = lineItems.Count,
                        TotalAmount = purchaseOrder.TotalAmount,
                        ConnectedToStock = req.ConnectToReceiveStock
                    },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return purchaseOrder.Id;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not create purchase order due to a database conflict.", ex);
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
            var timestamp = DateTimeOffset.UtcNow;
            var datePart = timestamp.ToString("yyyyMMdd");
            var timePart = timestamp.ToString("HHmmss");
            var randomPart = new Random().Next(100, 999).ToString();

            var baseOrderNumber = $"PO-{datePart}-{timePart}-{randomPart}";
            var orderNumber = baseOrderNumber;

            int attempts = 0;
            while (await _db.PurchaseOrders.AnyAsync(o => o.OrderNumber == orderNumber, ct) && attempts < 10)
            {
                randomPart = new Random().Next(100, 999).ToString();
                orderNumber = $"PO-{datePart}-{timePart}-{randomPart}";
                attempts++;
            }

            if (attempts >= 10)
                throw new ConflictException("Could not generate unique order number. Please try again.");

            return orderNumber;
        }

        #endregion
    }
}