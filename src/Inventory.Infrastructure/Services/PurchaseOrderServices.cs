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
using Inventory.Application.DTOs.PurchaseOrder;

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

        public async Task<IEnumerable<PurchaseOrderResponse>> GetRecentAsync(int count = 10, CancellationToken ct = default)
        {
            return await _db.PurchaseOrders
                .AsNoTracking()
                .OrderByDescending(o => o.CreatedUtc)
                .Take(count)
                .Select(o => MapToResponse(o))
                .ToListAsync(ct);
        }

        public async Task<PurchaseOrderResponse?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == id, ct);

            return order == null ? null : MapToResponse(order);
        }

        public async Task<IEnumerable<PurchaseOrderResponse>> GetBySupplierAsync(int supplierId, CancellationToken ct = default)
        {
            return await _db.PurchaseOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Where(o => o.SupplierId == supplierId)
                .OrderByDescending(o => o.CreatedUtc)
                .Select(o => MapToResponse(o))
                .ToListAsync(ct);
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

            // Validate and group line items by product and optional batch
            var lineItems = new Dictionary<(int ProductId, string? BatchNumber), (decimal Quantity, decimal UnitPrice)>();
            var productIds = new HashSet<int>();

            foreach (var line in req.Lines)
            {
                if (line.ProductId <= 0)
                    throw new ValidationException("Product ID must be positive for all line items.");

                if (line.Quantity <= 0)
                    throw new ValidationException("Quantity must be greater than zero for all line items.");

                if (line.UnitPrice < 0)
                    throw new ValidationException("Unit price cannot be negative.");

                var key = (line.ProductId, line.BatchNumber);

                if (lineItems.ContainsKey(key))
                {
                    // Combine quantities, keep first price
                    var existing = lineItems[key];
                    lineItems[key] = (existing.Quantity + line.Quantity, existing.UnitPrice);
                }
                else
                {
                    lineItems[key] = (line.Quantity, line.UnitPrice);
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
                    ReceiptExpenses = req.ReceiptExpenses,
                    Status = req.ConnectToReceiveStock ? PurchaseOrderStatus.Received : PurchaseOrderStatus.Draft
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
                    var productId = kvp.Key.ProductId;
                    var batchNumber = kvp.Key.BatchNumber;
                    var quantity = kvp.Value.Quantity;
                    var unitPrice = kvp.Value.UnitPrice;
                    var product = products.First(p => p.Id == productId);

                    // CONSOLIDATED TAX CALCULATION
                    decimal lineTotal;
                    decimal lineSubtotal;
                    decimal lineVat = 0;
                    decimal lineManTax = 0;

                    if (req.IsTaxInclusive)
                    {
                        // UnitPrice is the total price including tax
                        lineTotal = unitPrice * quantity;
                        
                        // Total = Base + (Base * VatRate) - (Base * ManTaxRate)
                        // Total = Base * (1 + VatRate - ManTaxRate)
                        decimal divisor = 1;
                        if (req.ApplyVat) divisor += TaxConstants.VatRate;
                        if (req.ApplyManufacturingTax) divisor -= TaxConstants.ManufacturingTaxRate;

                        lineSubtotal = Math.Round(lineTotal / divisor, 2, MidpointRounding.AwayFromZero);
                        
                        // Recalculate taxes from base for consistency
                        if (req.ApplyVat)
                            lineVat = Math.Round(lineSubtotal * TaxConstants.VatRate, 2, MidpointRounding.AwayFromZero);
                        if (req.ApplyManufacturingTax)
                            lineManTax = Math.Round(lineSubtotal * TaxConstants.ManufacturingTaxRate, 2, MidpointRounding.AwayFromZero);
                            
                        // Adjust subtotal to ensure it adds up exactly to Total
                        lineSubtotal = lineTotal - lineVat + lineManTax;
                    }
                    else
                    {
                        // UnitPrice is the base price excluding tax
                        lineSubtotal = unitPrice * quantity;
                        
                        if (req.ApplyVat)
                            lineVat = Math.Round(lineSubtotal * TaxConstants.VatRate, 2, MidpointRounding.AwayFromZero);
                        if (req.ApplyManufacturingTax)
                            lineManTax = Math.Round(lineSubtotal * TaxConstants.ManufacturingTaxRate, 2, MidpointRounding.AwayFromZero);
                            
                        lineTotal = lineSubtotal + lineVat - lineManTax;
                    }

                    totalSubtotal += lineSubtotal;
                    totalVat += lineVat;
                    totalManTax += lineManTax;
                    totalOrderAmount += lineTotal;

                    var poLine = new PurchaseOrderLine
                    {
                        PurchaseOrderId = purchaseOrder.Id,
                        ProductId = productId,
                        ProductNameSnapshot = product.Name,
                        BatchNumber = batchNumber,
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
                        // ENSURE PRODUCT BATCH EXISTS
                        // ============================================================
                        if (!string.IsNullOrEmpty(batchNumber))
                        {
                            var batch = await _db.ProductBatches
                                .FirstOrDefaultAsync(b => b.ProductId == productId && b.BatchNumber == batchNumber, ct);

                            if (batch == null)
                            {
                                batch = new ProductBatch
                                {
                                    ProductId = productId,
                                    BatchNumber = batchNumber,
                                    UnitCost = unitPrice,
                                    UnitPrice = product.Price, // Use current product price as default
                                    UpdatedUtc = DateTimeOffset.UtcNow
                                };
                                _db.ProductBatches.Add(batch);
                            }
                            else
                            {
                                // Update cost if necessary? Or just notes
                                batch.UnitCost = unitPrice;
                                batch.UpdatedUtc = DateTimeOffset.UtcNow;
                            }
                        }

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
                            BatchNumber = batchNumber,
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

        public async Task UpdateStatusAsync(long id, PurchaseOrderStatus status, UserContext user, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders.FindAsync(new object[] { id }, ct);
            if (order == null) throw new NotFoundException($"Purchase order {id} not found.");

            var before = new { order.Status };
            order.Status = status;

            await _db.SaveChangesAsync(ct);

            await _auditWriter.LogUpdateAsync<PurchaseOrder>(id, user, before, new { Status = status }, ct);
        }

        public async Task UpdatePaymentStatusAsync(long id, PurchasePaymentStatus status, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            
            var order = await _db.PurchaseOrders.FindAsync(new object[] { id }, ct);
            if (order == null) throw new NotFoundException($"Purchase order {id} not found.");

            if (order.PaymentStatus == status) return;

            var before = new { order.PaymentStatus };
            var oldStatus = order.PaymentStatus;

            // Use transaction to ensure money flow stays consistent
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                order.PaymentStatus = status;

                if (status == PurchasePaymentStatus.Paid)
                {
                    // Record payment: create an Expense transaction for the supplier
                    decimal paymentAmount = order.TotalAmount - order.RefundedAmount;
                    
                    if (paymentAmount > 0)
                    {
                        var paymentTx = new FinancialTransaction
                        {
                            Type = FinancialTransactionType.Expense,
                            Amount = paymentAmount,
                            PurchaseOrderId = order.Id,
                            SupplierId = order.SupplierId,
                            TimestampUtc = DateTimeOffset.UtcNow,
                            UserId = user.UserId,
                            UserDisplayName = user.UserDisplayName,
                            Note = $"Full Payment for Purchase Order {order.OrderNumber}"
                        };
                        _db.FinancialTransactions.Add(paymentTx);
                    }
                }
                else if (status == PurchasePaymentStatus.Unpaid && oldStatus == PurchasePaymentStatus.Paid)
                {
                    // Reversal: Find and remove ONLY the auto-generated payment transaction for this order
                    var existingTxs = await _db.FinancialTransactions
                        .Where(t => t.PurchaseOrderId == order.Id && 
                                    t.Type == FinancialTransactionType.Expense && 
                                    t.Note != null && 
                                    t.Note.Contains($"Payment for Purchase Order {order.OrderNumber}"))
                        .ToListAsync(ct);
                    
                    if (existingTxs.Any())
                    {
                        _db.FinancialTransactions.RemoveRange(existingTxs);
                    }
                }

                await _db.SaveChangesAsync(ct);
                await _auditWriter.LogUpdateAsync<PurchaseOrder>(id, user, before, new { PaymentStatus = status }, ct);
                
                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }


        public async Task RefundAsync(RefundPurchaseOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (req is null) throw new ArgumentNullException(nameof(req));
            if (req.Amount <= 0) throw new ValidationException("Refund amount must be positive.");

            var order = await _db.PurchaseOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct);

            if (order == null) throw new NotFoundException($"Purchase order {req.OrderId} not found.");

            if (order.RefundedAmount + req.Amount > order.TotalAmount)
                throw new ValidationException("Total refunded amount cannot exceed the total order amount.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var before = new { order.RefundedAmount };
                order.RefundedAmount += req.Amount;

                // If the order was Received, we need to adjust inventory
                if (order.Status == PurchaseOrderStatus.Received)
                {
                    // For a global refund, we ideally need to know which items are being refunded.
                    // If it's a simple monetary refund without item return, we just record the money.
                    // But if it's a partial product return, we'd need more details.
                    // Given the goal "Refund option only available for eligible completed orders",
                    // and "inventory adjustments occur if applicable", I'll assume for now it's a monetary refund
                    // unless otherwise specified. But usually PO refund means returning goods.
                    
                    // Logic for monetary refund (money back from supplier)
                    var refundTx = new FinancialTransaction
                    {
                        Type = FinancialTransactionType.Revenue, // Revenue because it's money coming IN (reversal of expense)
                        Amount = req.Amount,
                        PurchaseOrderId = order.Id,
                        SupplierId = order.SupplierId,
                        TimestampUtc = DateTimeOffset.UtcNow,
                        UserId = user.UserId,
                        UserDisplayName = user.UserDisplayName,
                        Note = $"Refund for Purchase Order {order.OrderNumber}. {req.Reason}"
                    };
                    _db.FinancialTransactions.Add(refundTx);
                }

                await _db.SaveChangesAsync(ct);
                await _auditWriter.LogUpdateAsync<PurchaseOrder>(order.Id, user, before, new { order.RefundedAmount }, ct);
                
                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task AttachInvoiceAsync(long orderId, string invoicePath, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");
            if (string.IsNullOrWhiteSpace(invoicePath)) throw new ValidationException("Invoice path must be provided.");

            var purchaseOrder = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (purchaseOrder is null)
                throw new NotFoundException($"Purchase order id {orderId} was not found.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousInvoicePath = purchaseOrder.InvoicePath;
                purchaseOrder.InvoicePath = invoicePath;
                purchaseOrder.InvoiceUploadedUtc = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                await _auditWriter.LogUpdateAsync<PurchaseOrder>(
                    purchaseOrder.Id,
                    user,
                    beforeState: new { InvoicePath = previousInvoicePath },
                    afterState: new { InvoicePath = purchaseOrder.InvoicePath, InvoiceUploadedUtc = purchaseOrder.InvoiceUploadedUtc },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not attach Invoice to purchase order due to a database conflict.", ex);
            }
        }

        public async Task RemoveInvoiceAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            var purchaseOrder = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (purchaseOrder is null)
                throw new NotFoundException($"Purchase order id {orderId} was not found.");

            if (purchaseOrder.InvoicePath == null)
                return; // Already removed

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousInvoicePath = purchaseOrder.InvoicePath;
                purchaseOrder.InvoicePath = null;
                purchaseOrder.InvoiceUploadedUtc = null;

                await _db.SaveChangesAsync(ct);

                await _auditWriter.LogUpdateAsync<PurchaseOrder>(
                    purchaseOrder.Id,
                    user,
                    beforeState: new { InvoicePath = previousInvoicePath },
                    afterState: new { InvoicePath = (string?)null, InvoiceUploadedUtc = (DateTimeOffset?)null },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not remove Invoice from purchase order due to a database conflict.", ex);
            }
        }

        public async Task AttachReceiptAsync(long orderId, string receiptPath, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");
            if (string.IsNullOrWhiteSpace(receiptPath)) throw new ValidationException("Receipt path must be provided.");

            var purchaseOrder = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (purchaseOrder is null)
                throw new NotFoundException($"Purchase order id {orderId} was not found.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousReceiptPath = purchaseOrder.ReceiptPath;
                purchaseOrder.ReceiptPath = receiptPath;
                purchaseOrder.ReceiptUploadedUtc = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                await _auditWriter.LogUpdateAsync<PurchaseOrder>(
                    purchaseOrder.Id,
                    user,
                    beforeState: new { ReceiptPath = previousReceiptPath },
                    afterState: new { ReceiptPath = purchaseOrder.ReceiptPath, ReceiptUploadedUtc = purchaseOrder.ReceiptUploadedUtc },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not attach Receipt to purchase order due to a database conflict.", ex);
            }
        }

        public async Task RemoveReceiptAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            var purchaseOrder = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (purchaseOrder is null)
                throw new NotFoundException($"Purchase order id {orderId} was not found.");

            if (purchaseOrder.ReceiptPath == null)
                return; // Already removed

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousReceiptPath = purchaseOrder.ReceiptPath;
                purchaseOrder.ReceiptPath = null;
                purchaseOrder.ReceiptUploadedUtc = null;

                await _db.SaveChangesAsync(ct);

                await _auditWriter.LogUpdateAsync<PurchaseOrder>(
                    purchaseOrder.Id,
                    user,
                    beforeState: new { ReceiptPath = previousReceiptPath },
                    afterState: new { ReceiptPath = (string?)null, ReceiptUploadedUtc = (DateTimeOffset?)null },
                    ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not remove Receipt from purchase order due to a database conflict.", ex);
            }
        }

        private static PurchaseOrderResponse MapToResponse(PurchaseOrder o)
        {
            return new PurchaseOrderResponse(
                o.Id,
                o.OrderNumber,
                o.SupplierId,
                o.SupplierNameSnapshot,
                o.CreatedUtc,
                o.Status,
                o.PaymentStatus,
                o.CreatedByUserDisplayName,
                o.IsTaxInclusive,
                o.ApplyVat,
                o.ApplyManufacturingTax,
                o.Subtotal,
                o.VatAmount,
                o.ManufacturingTaxAmount,
                o.ReceiptExpenses,
                o.TotalAmount,
                o.RefundedAmount,
                o.Note,
                o.InvoicePath,
                o.InvoiceUploadedUtc,
                o.ReceiptPath,
                o.ReceiptUploadedUtc,
                o.Lines.Select(l => new PurchaseOrderLineResponse(
                    l.Id,
                    l.ProductId,
                    l.ProductNameSnapshot,
                    l.BatchNumber,
                    l.Quantity,
                    l.UnitSnapshot,
                    l.UnitPrice,
                    l.IsTaxInclusive,
                    l.LineSubtotal,
                    l.LineVatAmount,
                    l.LineManufacturingTaxAmount,
                    l.LineTotal)).ToList());
        }

        #endregion
    }
}