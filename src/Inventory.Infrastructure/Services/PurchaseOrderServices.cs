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
using Inventory.Application.Exceptions;

using Inventory.Application.DTOs.Payment;

namespace Inventory.Infrastructure.Services
{
    public sealed class PurchaseOrderServices : IPurchaseOrderServices
    {
        private readonly AppDbContext _db;
        private readonly IInventoryServices _inventoryServices;
        private readonly IFinancialServices _financialServices;

        public PurchaseOrderServices(
            AppDbContext db,
            IInventoryServices inventoryServices,
            IFinancialServices financialServices)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _inventoryServices = inventoryServices ?? throw new ArgumentNullException(nameof(inventoryServices));
            _financialServices = financialServices ?? throw new ArgumentNullException(nameof(financialServices));
        }

        public async Task<IEnumerable<PurchaseOrderResponse>> GetRecentAsync(int count = 10, CancellationToken ct = default)
        {
            return await _db.PurchaseOrders
                .Include(o => o.Payments)
                .Where(o => o.Status != PurchaseOrderStatus.Cancelled)
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
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == id, ct);

            return order == null ? null : MapToResponse(order);
        }

        public async Task<IEnumerable<PurchaseOrderResponse>> GetBySupplierAsync(int supplierId, CancellationToken ct = default)
        {
            return await _db.PurchaseOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .Where(o => o.SupplierId == supplierId && o.Status != PurchaseOrderStatus.Cancelled)
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
                    OrderDate = req.OrderDate ?? DateTimeOffset.UtcNow,
                    CreatedByUserId = user.UserId,
                    CreatedByUserDisplayName = user.UserDisplayName,
                    // Treat the incoming DueDate as the initial supplier payment deadline
                    PaymentDeadline = req.DueDate,
                    Note = req.Note,
                    IsTaxInclusive = req.IsTaxInclusive,
                    ApplyVat = req.ApplyVat,
                    ApplyManufacturingTax = req.ApplyManufacturingTax,
                    ReceiptExpenses = req.ReceiptExpenses,
                    // Default status is Pending; stock is only affected when moved to Received.
                    Status = PurchaseOrderStatus.Pending
                    // PaymentStatus is derived from ledger - defaults to Unpaid
                };

                // Handle Historical Orders
                if (req.IsHistorical)
                {
                    purchaseOrder.IsHistorical = true;
                    purchaseOrder.IsStockProcessed = false;
                    
                    if (req.Status.HasValue)
                    {
                        purchaseOrder.Status = req.Status.Value;
                    }
                }

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

                // 4. Handle Initial Payment
                if (req.PaymentStatus == PurchasePaymentStatus.Paid)
                {
                    var payment = new PaymentRecord
                    {
                        PurchaseOrderId = purchaseOrder.Id,
                        Amount = purchaseOrder.TotalAmount,
                        PaymentDate = purchaseOrder.OrderDate,
                        PaymentMethod = req.PaymentMethod,
                        PaymentType = PaymentRecordType.Payment,
                        Reference = "INITIAL-PAYMENT",
                        CreatedByUserId = user.UserId
                    };
                    
                    purchaseOrder.Payments.Add(payment);
                    
                    // Also create financial transaction
                    await _financialServices.CreateFinancialTransactionFromPaymentAsync(payment, user, ct);
                }

                purchaseOrder.RecalculatePaymentStatus();
                await _db.SaveChangesAsync(ct);


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

        public async Task CancelAsync(long id, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Order ID must be positive.");

            var order = await _db.PurchaseOrders
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == id, ct);

            if (order == null) throw new NotFoundException($"Purchase order {id} not found.");

            if (order.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Order is already cancelled.");

            // Money condition: no net money held (ledger-based)
            var netCash = order.GetNetCash();
            if (netCash != 0)
            {
                throw new ValidationException($"Order cannot be cancelled while there is a financial imbalance. Net Cash: {netCash:C}. All payments must be fully refunded before cancellation.");
            }

            // Stock condition: all quantities must be fully reversed (RefundedQuantity == Quantity)
            // CRITICAL: We only enforce this if order was RECEIVED (issued).
            if (order.Status == PurchaseOrderStatus.Received)
            {
                var remainingStockQuantity = order.Lines.Sum(l => l.Quantity - l.RefundedQuantity);
                if (remainingStockQuantity > 0)
                {
                    throw new ValidationException($"Order cannot be cancelled while stock movement exists. Remaining stock to be reversed: {remainingStockQuantity}. Please process a full stock return first.");
                }
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousStatus = order.Status;
                order.Status = PurchaseOrderStatus.Cancelled;

                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not cancel purchase order due to a database conflict.", ex);
            }
        }

        public async Task UpdateStatusAsync(long id, PurchaseOrderStatus status, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default)
        {
            var order = await _db.PurchaseOrders.FindAsync(new object[] { id }, ct);
            if (order == null) throw new NotFoundException($"Purchase order {id} not found.");

            if (order.Status == status) return; // Idempotent

            if (status == PurchaseOrderStatus.Cancelled)
            {
                // All cancellation must go through CancelAsync to enforce invariants.
                throw new ValidationException("Direct status change to Cancelled is not allowed. Use the dedicated cancel operation after completing all refunds.");
            }

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
                    if (order.IsHistorical)
                    {
                        if (!order.IsStockProcessed)
                        {
                            await _inventoryServices.ProcessPurchaseOrderStockAsync(order.Id, user, order.CreatedUtc, ct);
                            order.IsStockProcessed = true;
                        }
                    }
                    else
                    {
                        await _inventoryServices.ProcessPurchaseOrderStockAsync(order.Id, user, timestamp, ct);
                    }
                }

                await _db.SaveChangesAsync(ct);
                
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

            if (order.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify the payment deadline of a cancelled order.");

            if (order.PaymentDeadline == newDeadline) return;

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousDeadline = order.PaymentDeadline;
                order.PaymentDeadline = newDeadline;

                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task AddPaymentAsync(long orderId, Inventory.Application.DTOs.Payment.CreatePaymentRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (req.Amount <= 0) throw new ValidationException("Payment amount must be greater than zero.");

            var order = await _db.PurchaseOrders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order == null) throw new NotFoundException($"Purchase order {orderId} not found.");

            if (order.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot add payments to a cancelled order.");

            var pending = order.GetPendingAmount();
            if (req.Amount > pending)
                throw new ValidationException($"Payment amount {req.Amount:C} exceeds pending amount {pending:C}.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var payment = new PaymentRecord
                {
                    OrderType = OrderType.PurchaseOrder,
                    PurchaseOrderId = orderId,
                    Amount = req.Amount,
                    PaymentDate = req.PaymentDate,
                    PaymentMethod = req.PaymentMethod,
                    PaymentType = PaymentRecordType.Payment,
                    Reference = req.Reference,
                    Note = req.Note,
                    CreatedByUserId = user.UserId
                };


                order.Payments.Add(payment);
                
                // Single Source of Truth: ensure derived status is updated
                order.RecalculatePaymentStatus();

                // Record in Financial Ledger - Requirement 1
                await _financialServices.CreateFinancialTransactionFromPaymentAsync(payment, user, ct);

                await _db.SaveChangesAsync(ct);
                

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
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct);

            if (order == null) throw new NotFoundException($"Purchase order {req.OrderId} not found.");

            if (order.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot process refunds for a cancelled order.");

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

            // Money refund: allowed when net paid > 0 (ledger-based). PaymentStatus is descriptive, not a gate.
            decimal netCash = order.GetNetCash();

            // Allow refunding 0 amount if only returning stock
            if (hasAmount)
            {
                if (netCash <= 0)
                    throw new ValidationException($"No refundable money found (Net Cash: {netCash:C}).");

                if (req.Amount > netCash)
                    throw new ValidationException($"Refund amount cannot exceed Net Cash held. Max refundable: {netCash:C}");
            }

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

                // Create PaymentRecord for Refund
                var refundPayment = new PaymentRecord
                {
                    OrderType = OrderType.PurchaseOrder,
                    PurchaseOrderId = order.Id,
                    Amount = req.Amount,
                    PaymentDate = DateTimeOffset.UtcNow,
                    PaymentMethod = PaymentMethod.Cash, // Default to cash for refunds
                    PaymentType = PaymentRecordType.Refund,
                    Note = $"Refund for Order {order.OrderNumber}. Reason: {req.Reason}",
                    CreatedByUserId = user.UserId
                };

                order.Payments.Add(refundPayment);

                // Update Order Totals
                order.RefundedAmount += req.Amount;
                
                // Recalculate status
                order.RecalculatePaymentStatus();

                // Process Financial Refund - Requirement 1
                await _financialServices.CreateFinancialTransactionFromPaymentAsync(refundPayment, user, ct);

                await _db.SaveChangesAsync(ct);
                
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

            if (purchaseOrder.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot attach an invoice to a cancelled order.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousInvoicePath = purchaseOrder.InvoicePath;
                purchaseOrder.InvoicePath = invoicePath;
                purchaseOrder.InvoiceUploadedUtc = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);


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

            if (purchaseOrder.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify documents for a cancelled order.");

            if (purchaseOrder.InvoicePath == null)
                return; // Already removed

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousInvoicePath = purchaseOrder.InvoicePath;
                purchaseOrder.InvoicePath = null;
                purchaseOrder.InvoiceUploadedUtc = null;

                await _db.SaveChangesAsync(ct);


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

            if (purchaseOrder.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot attach a receipt to a cancelled order.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousReceiptPath = purchaseOrder.ReceiptPath;
                purchaseOrder.ReceiptPath = receiptPath;
                purchaseOrder.ReceiptUploadedUtc = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);


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

            if (purchaseOrder.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify documents for a cancelled order.");

            if (purchaseOrder.ReceiptPath == null)
                return; // Already removed

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousReceiptPath = purchaseOrder.ReceiptPath;
                purchaseOrder.ReceiptPath = null;
                purchaseOrder.ReceiptUploadedUtc = null;

                await _db.SaveChangesAsync(ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not remove Receipt from purchase order due to a database conflict.", ex);
            }
        }

        public async Task ActivateStockAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            
            var order = await _db.PurchaseOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order == null) throw new NotFoundException($"Purchase Order {orderId} not found.");

            if (!order.IsHistorical)
                throw new ValidationException("Only historical orders can be manually activated.");

            if (order.IsStockProcessed)
                throw new ValidationException("Stock has already been processed for this order.");

            if (order.Status != PurchaseOrderStatus.Received)
                throw new ValidationException("Order must be in 'Received' status to activate stock.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Process stock using the Order Date (CreatedUtc) as the timestamp
                await _inventoryServices.ProcessPurchaseOrderStockAsync(order.Id, user, order.CreatedUtc, ct);
                
                order.IsStockProcessed = true;
                await _db.SaveChangesAsync(ct);
                
                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        public async Task UpdatePaymentInfoAsync(long id, UpdatePurchaseOrderPaymentRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Order ID must be positive.");
            if (req is null) throw new ArgumentNullException(nameof(req));
            if (req.OrderId != id) throw new ValidationException("Order ID mismatch.");

            var purchaseOrder = await _db.PurchaseOrders
                .FirstOrDefaultAsync(o => o.Id == id, ct);

            if (purchaseOrder is null)
                throw new NotFoundException($"Purchase order id {id} was not found.");

            if (purchaseOrder.Status == PurchaseOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify payment info for a cancelled order.");

            // Validate logic for checks
            if (purchaseOrder.PaymentMethod == PaymentMethod.Check || req.PaymentMethod == PaymentMethod.Check)
            {
                if (req.CheckReceived == true && !req.CheckReceivedDate.HasValue)
                    throw new ValidationException("Check issued date is required when marking check as issued.");
                
                if (req.CheckCashed == true && !req.CheckCashedDate.HasValue)
                    throw new ValidationException("Check cashed date is required when marking check as cashed.");
            }

            if (purchaseOrder.PaymentMethod == PaymentMethod.BankTransfer || req.PaymentMethod == PaymentMethod.BankTransfer)
            {
                if (purchaseOrder.PaymentStatus == PurchasePaymentStatus.Paid && string.IsNullOrWhiteSpace(req.TransferId) && string.IsNullOrWhiteSpace(purchaseOrder.TransferId))
                    throw new ValidationException("Transfer ID is required for bank transfer orders.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var beforeState = new 
                { 
                    purchaseOrder.PaymentMethod,
                    purchaseOrder.CheckReceived, 
                    purchaseOrder.CheckReceivedDate,
                    purchaseOrder.CheckCashed,
                    purchaseOrder.CheckCashedDate,
                    purchaseOrder.TransferId,
                    purchaseOrder.Note
                };

                if (req.PaymentMethod.HasValue)
                {
                    purchaseOrder.PaymentMethod = req.PaymentMethod.Value;
                }

                if (purchaseOrder.PaymentMethod == PaymentMethod.Check)
                {
                    purchaseOrder.CheckReceived = req.CheckReceived;
                    purchaseOrder.CheckReceivedDate = req.CheckReceivedDate;
                    purchaseOrder.CheckCashed = req.CheckCashed;
                    purchaseOrder.CheckCashedDate = req.CheckCashedDate;
                }
                else if (purchaseOrder.PaymentMethod == PaymentMethod.BankTransfer)
                {
                    purchaseOrder.TransferId = req.TransferId;
                }

                if (!string.IsNullOrWhiteSpace(req.Note))
                {
                    purchaseOrder.Note = req.Note;
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                throw;
            }
        }

        private static PurchaseOrderResponse MapToResponse(PurchaseOrder o)
        {
            // Collection Status
            var totalPaid = o.GetTotalPaid();
            var pending = o.GetPendingAmount();
            
            return new PurchaseOrderResponse
            {
                Id = o.Id,
                OrderNumber = o.OrderNumber,
                SupplierId = o.SupplierId,
                SupplierName = o.SupplierNameSnapshot,
                CreatedUtc = o.CreatedUtc,
                PaymentDeadline = o.PaymentDeadline,
                Status = o.Status,
                PaymentStatus = o.PaymentStatus,
                CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                IsTaxInclusive = o.IsTaxInclusive,
                ApplyVat = o.ApplyVat,
                ApplyManufacturingTax = o.ApplyManufacturingTax,
                Subtotal = o.Subtotal,
                VatAmount = o.VatAmount,
                ManufacturingTaxAmount = o.ManufacturingTaxAmount,
                ReceiptExpenses = o.ReceiptExpenses,
                TotalAmount = o.TotalAmount,
                RefundedAmount = o.RefundedAmount,
                Note = o.Note,
                IsHistorical = o.IsHistorical,
                IsStockProcessed = o.IsStockProcessed,
                InvoicePath = o.InvoicePath,
                InvoiceUploadedUtc = o.InvoiceUploadedUtc,
                ReceiptPath = o.ReceiptPath,
                ReceiptUploadedUtc = o.ReceiptUploadedUtc,
                PaymentMethod = o.PaymentMethod,
                CheckReceived = o.CheckReceived,
                CheckReceivedDate = o.CheckReceivedDate,
                CheckCashed = o.CheckCashed,
                CheckCashedDate = o.CheckCashedDate,
                TransferId = o.TransferId,
                PaidAmount = totalPaid,
                RemainingAmount = pending,
                DeservedAmount = o.GetDeservedAmount(),
                IsOverdue = o.IsOverdue(),
                Payments = o.Payments.OrderByDescending(p => p.PaymentDate).Select(p => new PaymentRecordDto
                {
                    Id = p.Id,
                    Amount = p.Amount,
                    PaymentDate = p.PaymentDate,
                    PaymentMethod = p.PaymentMethod,
                    PaymentType = p.PaymentType,
                    Reference = p.Reference,
                    Note = p.Note,
                    CreatedByUserId = p.CreatedByUserId
                }).ToList(),
                Lines = o.Lines.Select(l => new PurchaseOrderLineResponse(
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
                    l.RefundedQuantity)).ToList(),
                TotalPaid = totalPaid,
                TotalRefunded = o.GetTotalRefunded(),
                NetCash = o.GetNetCash(),
                PendingAmount = pending,
                RefundDue = o.GetRefundDue()
            };
        }

        #endregion
    }
}