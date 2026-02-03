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
        private readonly IInventoryServices _inventoryServices;
        private readonly IFinancialServices _financialServices;

        public PurchaseOrderServices(
            AppDbContext db,
            IAuditLogWriter auditWriter,
            IInventoryServices inventoryServices,
            IFinancialServices financialServices)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
            _inventoryServices = inventoryServices ?? throw new ArgumentNullException(nameof(inventoryServices));
            _financialServices = financialServices ?? throw new ArgumentNullException(nameof(financialServices));
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
                }

                purchaseOrder.Subtotal = totalSubtotal;
                purchaseOrder.VatAmount = totalVat;
                purchaseOrder.ManufacturingTaxAmount = totalManTax;
                purchaseOrder.TotalAmount = totalOrderAmount + req.ReceiptExpenses;

                await _db.SaveChangesAsync(ct);

                // Even if ConnectToReceiveStock is true, we should handle it via a separate call 
                // to maintain "no effects on creation" if we want to be strict, or just call the service.
                // The rule says "Stock must NOT be affected on: Order creation".
                // I will set it to Draft always on creation to enforce this, or just let it stay Received but NOT trigger stock here.
                // If I set it to Received but don't call the service, then stock won't be updated.
                // If I want to follow "Transitioning into the triggering state applies effects", then creating it as Received SHOULD trigger it.
                // But the rule explicitly says "Stock must NOT be affected on: Order creation".
                // So I will force status to Draft or Pending if it was Received.
                if (purchaseOrder.Status == PurchaseOrderStatus.Received)
                {
                    purchaseOrder.Status = PurchaseOrderStatus.Pending;
                }

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

            if (order.Status == status) return; // Idempotent

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousStatus = order.Status;

                // 1. Transitioning OUT of Received -> Reverse Effects
                if (previousStatus == PurchaseOrderStatus.Received)
                {
                    await _inventoryServices.ReversePurchaseOrderStockAsync(order.Id, user, ct);
                }

                // 2. Set new status
                order.Status = status;

                // 3. Transitioning INTO Received -> Apply Effects
                if (status == PurchaseOrderStatus.Received)
                {
                    await _inventoryServices.ProcessPurchaseOrderStockAsync(order.Id, user, ct);
                }

                await _db.SaveChangesAsync(ct);
                await _auditWriter.LogUpdateAsync<PurchaseOrder>(id, user, new { Status = previousStatus }, new { Status = status }, ct);
                
                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task UpdatePaymentStatusAsync(long id, PurchasePaymentStatus status, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            
            var order = await _db.PurchaseOrders.FindAsync(new object[] { id }, ct);
            if (order == null) throw new NotFoundException($"Purchase order {id} not found.");

            if (order.PaymentStatus == status) return;

            var oldStatus = order.PaymentStatus;

            // Use transaction to ensure money flow stays consistent
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // 1. Transitioning OUT of Paid -> Reverse effects
                if (oldStatus == PurchasePaymentStatus.Paid)
                {
                    await _financialServices.ReversePurchasePaymentAsync(order.Id, user, ct);
                }

                // 2. Set new status (FinancialServices might have already set it, but we ensure consistency)
                order.PaymentStatus = status;

                // 3. Transitioning INTO Paid -> Apply effects
                if (status == PurchasePaymentStatus.Paid)
                {
                    await _financialServices.ProcessPurchasePaymentAsync(order.Id, user, ct);
                }

                await _db.SaveChangesAsync(ct);
                await _auditWriter.LogUpdateAsync<PurchaseOrder>(id, user, new { PaymentStatus = oldStatus }, new { PaymentStatus = status }, ct);
                
                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task UpdatePaymentDeadlineAsync(long id, DateTimeOffset? newDeadline, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            var order = await _db.PurchaseOrders.FindAsync(new object[] { id }, ct);
            if (order == null) throw new NotFoundException($"Purchase order {id} not found.");

            if (order.PaymentDeadline == newDeadline) return;

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousDeadline = order.PaymentDeadline;
                order.PaymentDeadline = newDeadline;

                await _db.SaveChangesAsync(ct);
                await _auditWriter.LogUpdateAsync<PurchaseOrder>(id, user, new { PaymentDeadline = previousDeadline }, new { PaymentDeadline = newDeadline }, ct);

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


            var order = await _db.PurchaseOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct);

            if (order == null) throw new NotFoundException($"Purchase order {req.OrderId} not found.");

            // Validate Refund Input
            bool hasAmount = req.Amount > 0;
            bool hasLines = req.LineItems != null && req.LineItems.Any(l => l.Quantity > 0);

            if (!hasAmount && !hasLines)
                throw new ValidationException("You must specify either a refund amount or products to return.");

            if (req.Amount < 0)
                 throw new ValidationException("Refund amount cannot be negative.");

            // Independent Eligibility Validation (Stock vs Money)
            // Stock refund requires order to be Received
            if (hasLines && order.Status != PurchaseOrderStatus.Received)
                throw new ValidationException("Cannot refund stock before order is received.");
            
            // Money refund requires payment to be Paid
            if (hasAmount && order.PaymentStatus != PurchasePaymentStatus.Paid)
                throw new ValidationException("Cannot refund money before payment is completed.");

            // Validate Amount Cap
            decimal remainingRefundableAmount = order.TotalAmount - order.RefundedAmount;
            if (hasAmount && req.Amount > remainingRefundableAmount)
                throw new ValidationException($"Refund amount exceeds remaining refundable amount. Max refundable: {remainingRefundableAmount:C}");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var before = new { order.RefundedAmount };
                
                // Create Refund Audit
                var refundTx = new RefundTransaction
                {
                    Type = RefundType.PurchaseOrder,
                    PurchaseOrderId = order.Id,
                    Amount = req.Amount,
                    Reason = req.Reason,
                    ProcessedUtc = DateTimeOffset.UtcNow,
                    ProcessedByUserId = user.UserId,
                    ProcessedByUserDisplayName = user.UserDisplayName,
                    Note = $"Refund processed for Purchase Order {order.OrderNumber}"
                };

                // Line Item Processing
                if (req.LineItems != null && req.LineItems.Any())
                {
                    foreach (var refundItem in req.LineItems)
                    {
                        var line = order.Lines.FirstOrDefault(l => l.Id == refundItem.PurchaseOrderLineId);
                        if (line == null) 
                            throw new NotFoundException($"Purchase Order Line {refundItem.PurchaseOrderLineId} not found in Order {order.Id}.");

                        // Validate Quantity
                        if (refundItem.Quantity <= 0)
                            throw new ValidationException("Refund quantity must be positive.");

                        if (line.RefundedQuantity + refundItem.Quantity > line.Quantity)
                            throw new ValidationException($"Cannot refund {refundItem.Quantity} for product '{line.ProductNameSnapshot}'. Max refundable: {line.Quantity - line.RefundedQuantity}");

                        // Update Line state
                        line.RefundedQuantity += refundItem.Quantity;

                        // Add to Audit Transaction Lines
                        refundTx.Lines.Add(new RefundTransactionLine
                        {
                            PurchaseOrderLineId = line.Id,
                            ProductId = line.ProductId,
                            ProductNameSnapshot = line.ProductNameSnapshot,
                            Quantity = refundItem.Quantity,
                            BatchNumber = refundItem.BatchNumber ?? line.BatchNumber,
                            UnitPriceSnapshot = line.UnitPrice,
                            LineRefundAmount = refundItem.Quantity * line.UnitPrice 
                        });
                    }

                    // Process Stock Return (to Supplier = Reduce Stock)
                    await _inventoryServices.RefundPurchaseOrderStockAsync(order.Id, req.LineItems, user, ct);
                }
                // Else: Money only refund, no stock movement

                _db.RefundTransactions.Add(refundTx);

                // Update Order Totals
                order.RefundedAmount += req.Amount;

                // Process Financial Refund (Revenue)
                await _financialServices.ProcessPurchaseRefundPaymentAsync(order.Id, req.Amount, user, ct);

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
                o.PaymentDeadline,
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
                    l.LineTotal,
                    l.RefundedQuantity)).ToList());
        }

        #endregion
    }
}