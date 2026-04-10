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
using Inventory.Application.Exceptions;

namespace Inventory.Infrastructure.Services
{
    public sealed class SupplierSalesOrderServices : ISupplierSalesOrderServices
    {
        private const int MaxTake = 1000;
        private readonly AppDbContext _db;
        private readonly IInventoryServices _inventoryServices;
        private readonly IFinancialServices _financialServices;
        private readonly IReportingServices _reportingServices;

        public SupplierSalesOrderServices(
            AppDbContext db,
            IInventoryServices inventoryServices,
            IFinancialServices financialServices,
            IReportingServices reportingServices)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _inventoryServices = inventoryServices ?? throw new ArgumentNullException(nameof(inventoryServices));
            _financialServices = financialServices ?? throw new ArgumentNullException(nameof(financialServices));
            _reportingServices = reportingServices ?? throw new ArgumentNullException(nameof(reportingServices));
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

            // GUARD: Check Supplier Balance (NetOwedToSupplier)
            var balance = await _reportingServices.GetSupplierBalanceAsync(req.SupplierId, ct);
            
            // Validate and group line items to compute total for balance check
            var lineItems = new List<CreateSupplierSalesOrderLineRequest>(req.Lines);
            var productIds = lineItems.Select(l => l.ProductId).Distinct().ToList();
            var batchIds = lineItems.Where(l => l.ProductBatchId.HasValue).Select(l => l.ProductBatchId!.Value).Distinct().ToList();

            // Load products and batches for calculation
            var products = await _db.Products.AsNoTracking().Where(p => productIds.Contains(p.Id)).ToListAsync(ct);
            var batches = await _db.ProductBatches.AsNoTracking().Where(b => batchIds.Contains(b.Id)).ToListAsync(ct);

            decimal totalAmount = 0;
            foreach (var line in lineItems)
            {
                decimal lineTotal;
                if (req.IsTaxInclusive)
                {
                    lineTotal = line.UnitPrice * line.Quantity;
                }
                else
                {
                    decimal lineSubtotal = line.UnitPrice * line.Quantity;
                    decimal lineVat = req.ApplyVat ? Math.Round(lineSubtotal * TaxConstants.VatRate, 2, MidpointRounding.AwayFromZero) : 0;
                    decimal lineManTax = req.ApplyManufacturingTax ? Math.Round(lineSubtotal * TaxConstants.ManufacturingTaxRate, 2, MidpointRounding.AwayFromZero) : 0;
                    lineTotal = lineSubtotal + lineVat - lineManTax;
                }
                totalAmount += lineTotal;
            }

            if (totalAmount > balance.NetOwedToSupplier)
            {
                throw new ValidationException($"Cannot create order of {totalAmount:C}. Supplier balance is only {balance.NetOwedToSupplier:C}. " +
                    "Supplier Sales Orders are limited to the current Net Owed to Supplier.");
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
                    PaymentStatus = req.PaymentStatus,
                    CreatedByUserId = user.UserId,
                    CreatedByUserDisplayName = user.UserDisplayName,
                    Note = req.Note,
                    IsTaxInclusive = req.IsTaxInclusive,
                    ApplyVat = req.ApplyVat,
                    ApplyManufacturingTax = req.ApplyManufacturingTax,
                    Status = req.IsHistorical ? (req.Status ?? SalesOrderStatus.Done) : SalesOrderStatus.Pending
                };

                _db.SupplierSalesOrders.Add(order);
                await _db.SaveChangesAsync(ct);

                decimal totalSubtotal = 0;
                decimal totalVat = 0;
                decimal totalManTax = 0;
                decimal totalOrderCost = 0;

                foreach (var line in lineItems)
                {
                    var product = products.First(p => p.Id == line.ProductId);
                    var batch = line.ProductBatchId.HasValue ? batches.FirstOrDefault(b => b.Id == line.ProductBatchId) : null;
                    
                    decimal lineTotal;
                    decimal lineSubtotal;
                    decimal lineVat = 0;
                    decimal lineManTax = 0;

                    if (req.IsTaxInclusive)
                    {
                        lineTotal = line.UnitPrice * line.Quantity;
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
                        lineSubtotal = line.UnitPrice * line.Quantity;
                        if (req.ApplyVat)
                            lineVat = Math.Round(lineSubtotal * TaxConstants.VatRate, 2, MidpointRounding.AwayFromZero);
                        if (req.ApplyManufacturingTax)
                            lineManTax = Math.Round(lineSubtotal * TaxConstants.ManufacturingTaxRate, 2, MidpointRounding.AwayFromZero);
                            
                        lineTotal = lineSubtotal + lineVat - lineManTax;
                    }

                    totalSubtotal += lineSubtotal;
                    totalVat += lineVat;
                    totalManTax += lineManTax;

                    var orderLine = new SupplierSalesOrderLine
                    {
                        SupplierSalesOrderId = order.Id,
                        ProductId = line.ProductId,
                        ProductNameSnapshot = product.Name,
                        UnitSnapshot = product.Unit,
                        BatchNumber = line.BatchNumber ?? batch?.BatchNumber,
                        ProductBatchId = line.ProductBatchId,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice,
                        LineSubtotal = lineSubtotal,
                        LineVatAmount = lineVat,
                        LineManufacturingTaxAmount = lineManTax,
                        LineTotal = lineTotal
                    };

                    _db.SupplierSalesOrderLines.Add(orderLine);

                    // Track COGS
                    decimal unitCost = batch?.UnitCost ?? 0m; // Note: Product.AvgCost not available in current model
                    totalOrderCost += (unitCost * line.Quantity);
                }

                order.Subtotal = totalSubtotal;
                order.VatAmount = totalVat;
                order.ManufacturingTaxAmount = totalManTax;
                order.TotalAmount = totalAmount;

                // ─── P&L Impact (Requirement 6) ──────────────────────────────────────
                // Revenue: The netting amount we are gaining from the supplier.
                var revenueTx = new FinancialTransaction
                {
                    Type = FinancialTransactionType.Revenue,
                    SupplierSalesOrderId = order.Id,
                    Amount = order.TotalAmount,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = user.UserId,
                    UserDisplayName = user.UserDisplayName,
                    Note = $"Bookkeeping Revenue from SSO {order.OrderNumber}."
                };
                _db.FinancialTransactions.Add(revenueTx);

                // COGS (Expense): The original cost of the items sold back.
                var cogsTx = new FinancialTransaction
                {
                    Type = FinancialTransactionType.Expense,
                    SupplierSalesOrderId = order.Id,
                    Amount = totalOrderCost,
                    TimestampUtc = DateTimeOffset.UtcNow,
                    UserId = user.UserId,
                    UserDisplayName = user.UserDisplayName,
                    Note = $"COGS for SSO {order.OrderNumber}."
                };
                _db.FinancialTransactions.Add(cogsTx);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                return order.Id;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                if (ex is ValidationException) throw;
                throw new ConflictException("Could not create supplier sales order.", ex);
            }
        }

        public async Task<SupplierSalesOrderResponseDto?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            return await _db.SupplierSalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
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
                    PaymentStatus = o.PaymentStatus,
                    TotalAmount = o.TotalAmount,
                    Note = o.Note,
                    IsTaxInclusive = o.IsTaxInclusive,
                    ApplyVat = o.ApplyVat,
                    ApplyManufacturingTax = o.ApplyManufacturingTax,
                    Subtotal = o.Subtotal,
                    VatAmount = o.VatAmount,
                    ManufacturingTaxAmount = o.ManufacturingTaxAmount,
                    IsHistorical = o.Status == SalesOrderStatus.Done, // Logic simplified
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
                    PaymentStatus = o.PaymentStatus,
                    TotalAmount = o.TotalAmount
                })
                .ToListAsync(ct);
        }

        public async Task CancelAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            var order = await _db.SupplierSalesOrders
                .Include(o => o.Lines)
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
                
                // Note: Per requirement "no ledger writes" on cancellation, we do NOT 
                // reverse the financial transactions here. 
                // However, the P&L reports should typically filter out Cancelled orders 
                // or we should have reversed them. Sticking to literal requirement.
                
                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not cancel supplier sales order.", ex);
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
            while (await _db.SupplierSalesOrders.AsNoTracking().AnyAsync(o => o.OrderNumber == orderNumber, ct) && attempts < 10)
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
