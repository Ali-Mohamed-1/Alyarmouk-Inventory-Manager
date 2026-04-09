using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SupplierSalesOrder;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Domain.Constants;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.Exceptions;

namespace Inventory.Infrastructure.Services
{
    public sealed class SupplierSalesOrderServices : ISupplierSalesOrderServices
    {
        private const int MaxTake = 1000;
        private readonly AppDbContext _db;
        private readonly IInventoryServices _inventoryServices;
        private readonly IFinancialServices _financialServices;

        public SupplierSalesOrderServices(
            AppDbContext db,
            IInventoryServices inventoryServices,
            IFinancialServices financialServices)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _inventoryServices = inventoryServices ?? throw new ArgumentNullException(nameof(inventoryServices));
            _financialServices = financialServices ?? throw new ArgumentNullException(nameof(financialServices));
        }

        public async Task<long> CreateAsync(CreateSupplierSalesOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            if (!req.DueDate.HasValue)
                throw new ValidationException("Due date is required for a supplier sales order.");

            if (req.SupplierId <= 0)
                throw new ArgumentOutOfRangeException(nameof(req), "Supplier ID must be positive.");

            if (req.Lines is null || req.Lines.Count == 0)
                throw new ValidationException("Supplier sales order must have at least one line item.");

            // Verify supplier exists
            var supplier = await _db.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == req.SupplierId, ct);

            if (supplier is null)
                throw new NotFoundException($"Supplier id {req.SupplierId} was not found.");

            // Validate and group line items
            var lineItems = new Dictionary<(int ProductId, string? BatchNumber), (decimal Quantity, decimal UnitPrice, long? ProductBatchId)>();
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
                    var existing = lineItems[key];
                    lineItems[key] = (existing.Quantity + line.Quantity, existing.UnitPrice, line.ProductBatchId);
                }
                else
                {
                    lineItems[key] = (line.Quantity, line.UnitPrice, line.ProductBatchId);
                    productIds.Add(line.ProductId);
                }
            }

            // Load all products
            var products = await _db.Products
                .AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);

            if (products.Count != productIds.Count)
            {
                var missingIds = productIds.Except(products.Select(p => p.Id)).ToList();
                throw new NotFoundException($"Product(s) not found: {string.Join(", ", missingIds)}");
            }

            // Generate unique order number
            var orderNumber = await GenerateUniqueOrderNumberAsync(ct);

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var order = new SupplierSalesOrder
                {
                    OrderNumber = orderNumber,
                    SupplierId = req.SupplierId,
                    SupplierNameSnapshot = supplier.Name,
                    CreatedUtc = DateTimeOffset.UtcNow,
                    OrderDate = req.OrderDate ?? DateTimeOffset.UtcNow,
                    DueDate = req.DueDate!.Value,
                    PaymentMethod = req.PaymentMethod,
                    CheckReceived = req.PaymentMethod == PaymentMethod.Cash ? null : req.CheckReceived,
                    CheckReceivedDate = req.PaymentMethod == PaymentMethod.Cash ? null : req.CheckReceivedDate,
                    CheckCashed = req.PaymentMethod == PaymentMethod.Cash ? null : req.CheckCashed,
                    CheckCashedDate = req.PaymentMethod == PaymentMethod.Cash ? null : req.CheckCashedDate,
                    TransferId = req.PaymentMethod == PaymentMethod.BankTransfer ? req.TransferId : null,
                    CreatedByUserId = user.UserId,
                    CreatedByUserDisplayName = user.UserDisplayName,
                    Note = req.Note,
                    IsTaxInclusive = req.IsTaxInclusive,
                    ApplyVat = req.ApplyVat,
                    ApplyManufacturingTax = req.ApplyManufacturingTax
                };

                _db.SupplierSalesOrders.Add(order);
                await _db.SaveChangesAsync(ct);

                decimal totalSubtotal = 0;
                decimal totalVat = 0;
                decimal totalManTax = 0;
                decimal totalOrderAmount = 0;

                foreach (var kvp in lineItems)
                {
                    var productId = kvp.Key.ProductId;
                    var batchNumber = kvp.Key.BatchNumber;
                    var (quantity, unitPrice, productBatchId) = kvp.Value;
                    var product = products.First(p => p.Id == productId);

                    // Tax Calculation (Consolidated with SalesOrder logic)
                    decimal lineTotal;
                    decimal lineSubtotal;
                    decimal lineVat = 0;
                    decimal lineManTax = 0;

                    if (req.IsTaxInclusive)
                    {
                        lineTotal = unitPrice * quantity;
                        decimal divisor = 1;
                        if (req.ApplyVat) divisor += TaxConstants.VatRate;
                        if (req.ApplyManufacturingTax) divisor -= TaxConstants.ManufacturingTaxRate;

                        lineSubtotal = Math.Round(lineTotal / divisor, 2, MidpointRounding.AwayFromZero);
                        if (req.ApplyVat)
                            lineVat = Math.Round(lineSubtotal * TaxConstants.VatRate, 2, MidpointRounding.AwayFromZero);
                        if (req.ApplyManufacturingTax)
                            lineManTax = Math.Round(lineSubtotal * TaxConstants.ManufacturingTaxRate, 2, MidpointRounding.AwayFromZero);
                            
                        lineSubtotal = lineTotal - lineVat + lineManTax;
                    }
                    else
                    {
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

                    var orderLine = new SupplierSalesOrderLine
                    {
                        SupplierSalesOrderId = order.Id,
                        ProductId = productId,
                        ProductNameSnapshot = product.Name,
                        UnitSnapshot = product.Unit,
                        BatchNumber = batchNumber,
                        ProductBatchId = productBatchId,
                        Quantity = quantity,
                        UnitPrice = unitPrice,
                        LineSubtotal = lineSubtotal,
                        LineVatAmount = lineVat,
                        LineManufacturingTaxAmount = lineManTax,
                        LineTotal = lineTotal
                    };

                    _db.SupplierSalesOrderLines.Add(orderLine);
                }

                order.Subtotal = totalSubtotal;
                order.VatAmount = totalVat;
                order.ManufacturingTaxAmount = totalManTax;
                order.TotalAmount = totalOrderAmount;

                if (req.IsHistorical)
                {
                    order.Status = req.Status ?? SalesOrderStatus.Done;
                }

                // Initial full payment handling if requested
                if (req.PaymentStatus == PaymentStatus.Paid)
                {
                    var payment = new PaymentRecord
                    {
                        OrderType = OrderType.SupplierSalesOrder,
                        SupplierSalesOrderId = order.Id,
                        Amount = order.TotalAmount,
                        PaymentDate = order.OrderDate,
                        PaymentMethod = order.PaymentMethod,
                        PaymentType = PaymentRecordType.Payment,
                        Note = "Full payment recorded at order creation.",
                        CreatedByUserId = user.UserId
                    };

                    order.Payments.Add(payment);
                    order.RecalculatePaymentStatus();
                    await _financialServices.CreateFinancialTransactionFromPaymentAsync(payment, user, ct);
                }
                else
                {
                    // Ensure status is initialized via RecalculatePaymentStatus() even if no payments
                    order.RecalculatePaymentStatus();
                }

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                // Stock reservation if Pending and not historical
                if (!req.IsHistorical && order.Status == SalesOrderStatus.Pending)
                {
                    // Note: This would typically call a new method in InventoryServices
                    // For now, we follow the pattern but I won't implement the inventory side unless required.
                    // await _inventoryServices.ReserveSupplierSalesOrderStockAsync(order.Id, user, ct);
                }

                return order.Id;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not create supplier sales order.", ex);
            }
        }

        public async Task<SupplierSalesOrderResponseDto?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            return await _db.SupplierSalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .Where(o => o.Id == id)
                .Select(o => new SupplierSalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    SupplierId = o.SupplierId,
                    SupplierName = o.SupplierNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    OrderDate = o.OrderDate,
                    DueDate = o.DueDate,
                    Status = o.Status,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    TotalAmount = o.TotalAmount,
                    TotalPaid = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount),
                    TotalRefunded = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    NetCash = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                              o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    PendingAmount = o.TotalAmount - (o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount)) > 0 
                                    ? o.TotalAmount - (o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount)) 
                                    : 0,
                    RefundDue = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount > 0
                                ? o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                  o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount
                                : 0,
                    CheckReceived = o.CheckReceived,
                    CheckReceivedDate = o.CheckReceivedDate,
                    CheckCashed = o.CheckCashed,
                    CheckCashedDate = o.CheckCashedDate,
                    TransferId = o.TransferId,
                    Note = o.Note,
                    IsTaxInclusive = o.IsTaxInclusive,
                    ApplyVat = o.ApplyVat,
                    ApplyManufacturingTax = o.ApplyManufacturingTax,
                    Subtotal = o.Subtotal,
                    VatAmount = o.VatAmount,
                    ManufacturingTaxAmount = o.ManufacturingTaxAmount,
                    RefundedAmount = o.RefundedAmount,
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
                    Lines = o.Lines.Select(l => new SupplierSalesOrderLineResponseDto
                    {
                        Id = l.Id,
                        ProductId = l.ProductId,
                        ProductName = l.ProductNameSnapshot,
                        Quantity = l.Quantity,
                        Unit = l.UnitSnapshot,
                        UnitPrice = l.UnitPrice,
                        BatchNumber = l.BatchNumber,
                        ProductBatchId = l.ProductBatchId,
                        LineSubtotal = l.LineSubtotal,
                        LineVatAmount = l.LineVatAmount,
                        LineManufacturingTaxAmount = l.LineManufacturingTaxAmount,
                        LineTotal = l.LineTotal,
                        RefundedQuantity = l.RefundedQuantity
                    }).ToList()
                })
                .SingleOrDefaultAsync(ct);
        }

        public async Task<IReadOnlyList<SupplierSalesOrderResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.SupplierSalesOrders
                .AsNoTracking()
                .Include(o => o.Payments)
                .Where(o => o.Status != SalesOrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedUtc)
                .Take(take)
                .Select(o => new SupplierSalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    SupplierId = o.SupplierId,
                    SupplierName = o.SupplierNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    OrderDate = o.OrderDate,
                    DueDate = o.DueDate,
                    Status = o.Status,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    TotalAmount = o.TotalAmount,
                    NetCash = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                              o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    PendingAmount = o.TotalAmount - (o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount)) > 0 
                                    ? o.TotalAmount - (o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount)) 
                                    : 0
                })
                .ToListAsync(ct);
        }

        public async Task AddPaymentAsync(long orderId, Inventory.Application.DTOs.Payment.CreatePaymentRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (req.Amount <= 0) throw new ValidationException("Payment amount must be greater than zero.");

            var order = await _db.SupplierSalesOrders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order == null) throw new NotFoundException($"Supplier sales order {orderId} not found.");

            if (order.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot add payments to a cancelled order.");

            var pending = order.GetPendingAmount();
            if (req.Amount > pending)
                throw new ValidationException($"Payment amount {req.Amount:C} exceeds pending amount {pending:C}.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var payment = new PaymentRecord
                {
                    OrderType = OrderType.SupplierSalesOrder,
                    SupplierSalesOrderId = orderId,
                    Amount = req.Amount,
                    PaymentDate = req.PaymentDate,
                    PaymentMethod = req.PaymentMethod,
                    PaymentType = PaymentRecordType.Payment,
                    Reference = req.Reference,
                    Note = req.Note,
                    CreatedByUserId = user.UserId
                };

                order.Payments.Add(payment);
                order.RecalculatePaymentStatus();

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

        public async Task CancelAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            var order = await _db.SupplierSalesOrders
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order is null) throw new NotFoundException($"Supplier sales order {orderId} not found.");

            if (order.Status == SalesOrderStatus.Cancelled) throw new ValidationException("Order is already cancelled.");

            // Use entity guard for cancellation
            if (!order.CanCancel(out string error))
                throw new ValidationException(error);

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                order.Status = SalesOrderStatus.Cancelled;
                await _db.SaveChangesAsync(ct);

                // Release stock if it was reserved (future implementation)
                // await _inventoryServices.ReleaseSupplierSalesOrderReservationAsync(order.Id, user, ct);

                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not cancel supplier sales order.", ex);
            }
        }

        public async Task RefundAsync(RefundSupplierSalesOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (req is null) throw new ArgumentNullException(nameof(req));

            var order = await _db.SupplierSalesOrders
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == req.SupplierSalesOrderId, ct);

            if (order is null) throw new NotFoundException($"Supplier sales order {req.SupplierSalesOrderId} not found.");

            // Use entity guard for refund
            if (!order.CanRefund(req.Amount, out string error))
                throw new ValidationException(error);

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Create Refund Transaction (Audit record)
                var refundTx = new RefundTransaction
                {
                    Type = RefundType.SupplierSalesOrder,
                    SupplierSalesOrderId = order.Id,
                    Amount = req.Amount,
                    Reason = req.Reason,
                    ProcessedUtc = DateTimeOffset.UtcNow,
                    ProcessedByUserId = user.UserId,
                    ProcessedByUserDisplayName = user.UserDisplayName,
                    Note = req.Reason
                };

                // Line Item Processing (Product Refund/Stock Return)
                if (req.LineItems != null && req.LineItems.Any())
                {
                    foreach (var refundItem in req.LineItems)
                    {
                        var line = order.Lines.FirstOrDefault(l => l.Id == refundItem.SupplierSalesOrderLineId);
                        if (line == null)
                            throw new ValidationException($"Line {refundItem.SupplierSalesOrderLineId} not found in order {order.Id}.");

                        if (refundItem.Quantity <= 0)
                            throw new ValidationException("Refund quantity must be positive.");

                        if (line.RefundedQuantity + refundItem.Quantity > line.Quantity)
                            throw new ValidationException($"Cannot refund {refundItem.Quantity} for product '{line.ProductNameSnapshot}'. Max refundable: {line.Quantity - line.RefundedQuantity}");

                        line.RefundedQuantity += refundItem.Quantity;

                        refundTx.Lines.Add(new RefundTransactionLine
                        {
                            SupplierSalesOrderLineId = line.Id,
                            ProductId = line.ProductId,
                            ProductNameSnapshot = line.ProductNameSnapshot,
                            Quantity = refundItem.Quantity,
                            BatchNumber = refundItem.BatchNumber ?? line.BatchNumber,
                            ProductBatchId = refundItem.ProductBatchId ?? line.ProductBatchId,
                            UnitPriceSnapshot = line.UnitPrice,
                            LineRefundAmount = refundItem.Quantity * line.UnitPrice
                        });
                    }

                    // Stock Return logic (would typically be in InventoryServices)
                    // await _inventoryServices.RefundSupplierSalesOrderStockAsync(order.Id, req.LineItems, user, ct);
                }

                _db.RefundTransactions.Add(refundTx);

                // Create PaymentRecord for Refund (Ledger record)
                var refundPayment = new PaymentRecord
                {
                    OrderType = OrderType.SupplierSalesOrder,
                    SupplierSalesOrderId = order.Id,
                    Amount = req.Amount,
                    PaymentDate = DateTimeOffset.UtcNow,
                    PaymentMethod = order.PaymentMethod,
                    PaymentType = PaymentRecordType.Refund,
                    Note = $"Refund for SSO {order.OrderNumber}. Reason: {req.Reason}",
                    CreatedByUserId = user.UserId
                };

                order.Payments.Add(refundPayment);
                order.RefundedAmount += req.Amount;
                order.RecalculatePaymentStatus();

                // Record Financial ledger impact
                await _financialServices.CreateFinancialTransactionFromPaymentAsync(refundPayment, user, ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not process refund for supplier sales order.", ex);
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

            var orderNumber = $"SSO-{datePart}-{timePart}-{randomPart}";

            int attempts = 0;
            while (await _db.SupplierSalesOrders.AnyAsync(o => o.OrderNumber == orderNumber, ct) && attempts < 10)
            {
                randomPart = new Random().Next(100, 999).ToString();
                orderNumber = $"SSO-{datePart}-{timePart}-{randomPart}";
                attempts++;
            }

            if (attempts >= 10)
                throw new ConflictException("Could not generate unique order number.");

            return orderNumber;
        }

        #endregion
    }
}
