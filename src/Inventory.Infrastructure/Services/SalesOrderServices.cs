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
using Inventory.Application.DTOs.Transaction;
using Inventory.Application.DTOs.Payment;
using Inventory.Application.Exceptions;


namespace Inventory.Infrastructure.Services
{
    public sealed class SalesOrderServices : ISalesOrderServices
    {

        private const int MaxTake = 1000;
        private readonly AppDbContext _db;
        private readonly IInventoryServices _inventoryServices;
        private readonly IFinancialServices _financialServices;

        public SalesOrderServices(
            AppDbContext db,
            IInventoryServices inventoryServices,
            IFinancialServices financialServices)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
            _inventoryServices = inventoryServices ?? throw new ArgumentNullException(nameof(inventoryServices));
            _financialServices = financialServices ?? throw new ArgumentNullException(nameof(financialServices));
        }

        public async Task<long> CreateAsync(CreateSalesOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            if (req is null) throw new ArgumentNullException(nameof(req));
            ValidateUser(user);

            // Money flow / payment validation
            if (!req.DueDate.HasValue)
                throw new ValidationException("Due date is required for a sales order.");

            if (req.PaymentMethod == PaymentMethod.Check)
            {
                // For check payments, track whether we received and cashed the check with corresponding dates.
                if (req.CheckReceived == true && !req.CheckReceivedDate.HasValue)
                    throw new ValidationException("Check received date is required when the check is marked as received.");

                if (req.CheckCashed == true && !req.CheckCashedDate.HasValue)
                    throw new ValidationException("Check cashed date is required when the check is marked as cashed.");
            }
            
            if (req.PaymentMethod == PaymentMethod.BankTransfer)
            {
                if (req.PaymentStatus == PaymentStatus.Paid && string.IsNullOrWhiteSpace(req.TransferId))
                    throw new ValidationException("Transfer ID is required for bank transfer when payment status is Paid.");
            }

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

            // Validate and group line items by product and optional batch (so each batch has its own quantity)
            // Store quantity, price and batch number; keep first price per (product,batch) pair.
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
                    var existing = lineItems[key];
                    lineItems[key] = (existing.Quantity + line.Quantity, existing.UnitPrice);
                }
                else
                {
                    lineItems[key] = (line.Quantity, line.UnitPrice);
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

            // Verify sufficient stock for all products (Optional at creation, but good to check)
            /* 
            // We can keep this as a soft check or remove it if we want to allow drafting orders without stock
            foreach (var kvp in lineItems)
            {
                var productId = kvp.Key.ProductId;
                var quantity = kvp.Value.Quantity;

                var snapshot = stockSnapshots.FirstOrDefault(s => s.ProductId == productId);
                var availableStock = snapshot?.OnHand ?? 0;

                if (availableStock < quantity)
                {
                    var product = products.First(p => p.Id == productId);
                    throw new ValidationException($"Insufficient stock for product '{product.Name}'. Available: {availableStock}, Requested: {quantity}");
                }
            }
            */

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
                    OrderDate = req.OrderDate ?? DateTimeOffset.UtcNow,
                    DueDate = req.DueDate!.Value,
                    PaymentMethod = req.PaymentMethod,
                    // PaymentStatus is derived from ledger - defaults to Pending
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

                _db.SalesOrders.Add(salesOrder);
                await _db.SaveChangesAsync(ct); // Save to get the ID

                var inventoryTransactions = new List<InventoryTransaction>();

                decimal totalVat = 0;
                decimal totalManTax = 0;
                decimal totalSubtotal = 0;
                decimal totalOrderAmount = 0;

                // Create order lines
                foreach (var kvp in lineItems)
                {
                    var productId = kvp.Key.ProductId;
                    var batchNumber = kvp.Key.BatchNumber;
                    var (quantity, unitPrice) = kvp.Value;
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

                    // Lookup ProductBatchId if available
                    long? productBatchId = null;
                    if (!string.IsNullOrEmpty(batchNumber))
                    {
                        var batch = await _db.ProductBatches
                            .AsNoTracking()
                            .FirstOrDefaultAsync(b => b.ProductId == productId && b.BatchNumber == batchNumber, ct);
                        productBatchId = batch?.Id;
                    }

                    // Create order line
                    var orderLine = new SalesOrderLine
                    {
                        SalesOrderId = salesOrder.Id,
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

                    _db.SalesOrderLines.Add(orderLine);
                }

                // 3. Update Order Totals
                salesOrder.Subtotal = totalSubtotal;
                salesOrder.VatAmount = totalVat;
                salesOrder.ManufacturingTaxAmount = totalManTax;
                salesOrder.TotalAmount = totalOrderAmount;

                // 4. Handle Historical Status
                if (req.IsHistorical)
                {
                    salesOrder.IsHistorical = true;
                    salesOrder.IsStockProcessed = false;
                    // Allow creating as Done/Cancelled directly
                    if (req.Status.HasValue) 
                    {
                        salesOrder.Status = req.Status.Value;
                    }
                }
                
                // 5. Handle Initial Payment (if requested as Paid)
                if (req.PaymentStatus == PaymentStatus.Paid)
                {
                    var payment = new PaymentRecord
                    {
                        OrderType = OrderType.SalesOrder,
                        SalesOrderId = salesOrder.Id,
                        Amount = salesOrder.TotalAmount,
                        PaymentDate = salesOrder.OrderDate, // Use the order date specifically
                        PaymentMethod = salesOrder.PaymentMethod,
                        PaymentType = PaymentRecordType.Payment,
                        Note = "Full payment recorded at order creation.",
                        CreatedByUserId = user.UserId
                    };

                    salesOrder.Payments.Add(payment);

                    // Ensure status is correctly set/derived
                    salesOrder.RecalculatePaymentStatus();

                    await _financialServices.CreateFinancialTransactionFromPaymentAsync(payment, user, ct);
                }


                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                // 2. Reserve stock if order is Pending (Done is handled by inventory processing already if requested)
                // SKIP RESERVATION FOR HISTORICAL ORDERS
                if (!salesOrder.IsHistorical && salesOrder.Status == SalesOrderStatus.Pending)
                {
                    await _inventoryServices.ReserveSalesOrderStockAsync(salesOrder.Id, user, ct);
                }

                return salesOrder.Id;
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not create sales order due to a database conflict.", ex);
            }
        }

        public async Task AddPaymentAsync(long orderId, Inventory.Application.DTOs.Payment.CreatePaymentRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (req.Amount <= 0) throw new ValidationException("Payment amount must be greater than zero.");

            var order = await _db.SalesOrders
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order == null) throw new NotFoundException($"Sales order {orderId} not found.");

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
                    OrderType = OrderType.SalesOrder,
                    SalesOrderId = orderId,
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

        public async Task<SalesOrderResponseDto?> GetByIdAsync(long id, CancellationToken ct = default)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id), "Id must be positive.");

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .Where(o => o.Id == id)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    OrderDate = o.OrderDate,
                    DueDate = o.DueDate,
                    Status = o.Status,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    
                    // Inline calculations for EF Core translation
                    TotalPaid = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount),
                    TotalRefunded = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    NetCash = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                              o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    PendingAmount = o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) > 0 
                                    ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) 
                                    : 0,
                    RefundDue = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount > 0
                                ? o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                  o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount
                                : 0,
                    
                    // Legacy / UI mapping
                    PaidAmount = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount), // Show total collected
                    RemainingAmount = o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) > 0 
                                      ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) 
                                      : 0,
                    DeservedAmount = o.DueDate < DateTimeOffset.UtcNow && o.PaymentStatus != PaymentStatus.Paid 
                                     ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount)
                                     : 0,
                    IsOverdue = o.DueDate < DateTimeOffset.UtcNow && o.PaymentStatus != PaymentStatus.Paid,
                    
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
                    CheckReceived = o.CheckReceived,
                    CheckReceivedDate = o.CheckReceivedDate,
                    CheckCashed = o.CheckCashed,
                    CheckCashedDate = o.CheckCashedDate,
                    TransferId = o.TransferId,
                    InvoicePath = o.InvoicePath,
                    InvoiceUploadedUtc = o.InvoiceUploadedUtc,
                    ReceiptPath = o.ReceiptPath,
                    ReceiptUploadedUtc = o.ReceiptUploadedUtc,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    IsTaxInclusive = o.IsTaxInclusive,
                    ApplyVat = o.ApplyVat,
                    ApplyManufacturingTax = o.ApplyManufacturingTax,
                    Subtotal = o.Subtotal,
                    VatAmount = o.VatAmount,
                    ManufacturingTaxAmount = o.ManufacturingTaxAmount,
                    TotalAmount = o.TotalAmount,
                    IsHistorical = o.IsHistorical,
                    IsStockProcessed = o.IsStockProcessed,
                    RefundedAmount = o.RefundedAmount,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
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

        public async Task<IReadOnlyList<SalesOrderResponseDto>> GetCustomerOrdersAsync(int customerId, int take = 100, CancellationToken ct = default)
        {
            if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId), "Customer ID must be positive.");
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .Where(o => o.CustomerId == customerId && o.Status != SalesOrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedUtc)
                .Take(take)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    OrderDate = o.OrderDate,
                    DueDate = o.DueDate,
                    Status = o.Status,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    
                    // Inline calculations for EF Core translation
                    TotalPaid = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount),
                    TotalRefunded = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    NetCash = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                              o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    PendingAmount = o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) > 0 
                                    ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) 
                                    : 0,
                    RefundDue = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount > 0
                                ? o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                  o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount
                                : 0,
                    
                    // Legacy / UI mapping
                    PaidAmount = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount),
                    RemainingAmount = o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) > 0 
                                      ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) 
                                      : 0,
                    DeservedAmount = o.DueDate < DateTimeOffset.UtcNow && o.PaymentStatus != PaymentStatus.Paid 
                                     ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount)
                                     : 0,
                    IsOverdue = o.DueDate < DateTimeOffset.UtcNow && o.PaymentStatus != PaymentStatus.Paid,
                    
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
                    CheckReceived = o.CheckReceived,
                    CheckReceivedDate = o.CheckReceivedDate,
                    CheckCashed = o.CheckCashed,
                    CheckCashedDate = o.CheckCashedDate,
                    TransferId = o.TransferId,
                    InvoicePath = o.InvoicePath,
                    InvoiceUploadedUtc = o.InvoiceUploadedUtc,
                    ReceiptPath = o.ReceiptPath,
                    ReceiptUploadedUtc = o.ReceiptUploadedUtc,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    IsTaxInclusive = o.IsTaxInclusive,
                    ApplyVat = o.ApplyVat,
                    ApplyManufacturingTax = o.ApplyManufacturingTax,
                    Subtotal = o.Subtotal,
                    VatAmount = o.VatAmount,
                    ManufacturingTaxAmount = o.ManufacturingTaxAmount,
                    TotalAmount = o.TotalAmount,
                    RefundedAmount = o.RefundedAmount,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
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
                .ToListAsync(ct);
        }

        public async Task<IReadOnlyList<SalesOrderResponseDto>> GetRecentAsync(int take = 50, CancellationToken ct = default)
        {
            take = Math.Clamp(take, 1, MaxTake);

            return await _db.SalesOrders
                .AsNoTracking()
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .Where(o => o.Status != SalesOrderStatus.Cancelled)
                .OrderByDescending(o => o.CreatedUtc)
                .Take(take)
                .Select(o => new SalesOrderResponseDto
                {
                    Id = o.Id,
                    OrderNumber = o.OrderNumber,
                    CustomerId = o.CustomerId,
                    CustomerName = o.CustomerNameSnapshot,
                    CreatedUtc = o.CreatedUtc,
                    OrderDate = o.OrderDate,
                    DueDate = o.DueDate,
                    Status = o.Status,
                    PaymentMethod = o.PaymentMethod,
                    PaymentStatus = o.PaymentStatus,
                    
                    // Inline calculations for EF Core translation
                    TotalPaid = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount),
                    TotalRefunded = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    NetCash = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                              o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount),
                    PendingAmount = o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) > 0 
                                    ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) 
                                    : 0,
                    RefundDue = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount > 0
                                ? o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - 
                                  o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount) - o.TotalAmount
                                : 0,
                    
                    // Legacy / UI mapping
                    PaidAmount = o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount),
                    RemainingAmount = o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) > 0 
                                      ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) 
                                      : 0,
                    DeservedAmount = o.DueDate < DateTimeOffset.UtcNow && o.PaymentStatus != PaymentStatus.Paid 
                                     ? o.TotalAmount - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount)
                                     : 0,
                    IsOverdue = o.DueDate < DateTimeOffset.UtcNow && o.PaymentStatus != PaymentStatus.Paid,
                    
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
                    CheckReceived = o.CheckReceived,
                    CheckReceivedDate = o.CheckReceivedDate,
                    CheckCashed = o.CheckCashed,
                    CheckCashedDate = o.CheckCashedDate,
                    TransferId = o.TransferId,
                    InvoicePath = o.InvoicePath,
                    InvoiceUploadedUtc = o.InvoiceUploadedUtc,
                    ReceiptPath = o.ReceiptPath,
                    ReceiptUploadedUtc = o.ReceiptUploadedUtc,
                    CreatedByUserDisplayName = o.CreatedByUserDisplayName,
                    Note = o.Note,
                    IsTaxInclusive = o.IsTaxInclusive,
                    ApplyVat = o.ApplyVat,
                    ApplyManufacturingTax = o.ApplyManufacturingTax,
                    Subtotal = o.Subtotal,
                    VatAmount = o.VatAmount,
                    ManufacturingTaxAmount = o.ManufacturingTaxAmount,
                    TotalAmount = o.TotalAmount,
                    RefundedAmount = o.RefundedAmount,
                    Lines = o.Lines.Select(l => new SalesOrderLineResponseDto
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
                .ToListAsync(ct);
        }

        public async Task CompleteOrderAsync(long orderId, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default)
        {
            await UpdateStatusAsync(orderId, new UpdateSalesOrderStatusRequest { OrderId = orderId, Status = SalesOrderStatus.Done }, user, timestamp, ct);
        }

        public async Task CancelAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            var order = await _db.SalesOrders
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (order is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (order.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Order is already cancelled.");

            // Money condition: no net money held (ledger-based)
            var netCash = order.GetNetCash();
            if (netCash != 0)
            {
                throw new ValidationException($"Order cannot be cancelled while there is a financial imbalance. Net Cash: {netCash:C}. All payments must be fully refunded before cancellation.");
            }

            // Stock condition: all quantities must be fully reversed (RefundedQuantity == Quantity)
            // CRITICAL: We only enforce this if order was DONE (issued). 
            // If it was PENDING, stock is just reserved and CancelAsync (or the trigger) should handle releasing reservations.
            if (order.Status == SalesOrderStatus.Done)
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
                order.Status = SalesOrderStatus.Cancelled;

                await _db.SaveChangesAsync(ct);


                await _db.SaveChangesAsync(ct);

                // Release reservations if it was Pending
                if (previousStatus == SalesOrderStatus.Pending)
                {
                    await _inventoryServices.ReleaseSalesOrderReservationAsync(order.Id, user, ct);
                }

                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not cancel sales order due to a database conflict.", ex);
            }
        }

        public async Task UpdateStatusAsync(long orderId, UpdateSalesOrderStatusRequest req, UserContext user, DateTimeOffset? timestamp = null, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");
            if (req is null) throw new ArgumentNullException(nameof(req));

            // Load the order with lines and products
            var salesOrder = await _db.SalesOrders
                .Include(o => o.Lines)
                .ThenInclude(l => l.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            // Validate status transition
            if (salesOrder.Status == req.Status)
                return; // Idempotent

            if (req.Status == SalesOrderStatus.Cancelled)
            {
                // All cancellation must go through CancelAsync to enforce invariants.
                throw new ValidationException("Direct status change to Cancelled is not allowed. Use the dedicated cancel operation after completing all refunds.");
            }

            if (salesOrder.Status == SalesOrderStatus.Done && req.Status != SalesOrderStatus.Done)
            {
                // Reversing from Done is allowed
            }
            else if (salesOrder.Status == SalesOrderStatus.Cancelled)
            {
                 throw new ValidationException("Cannot change status of a cancelled order.");
            }

            // Use transaction to ensure all operations are atomic
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousStatus = salesOrder.Status;

                // 1. Transitioning OUT of Done -> Reverse effects
                if (previousStatus == SalesOrderStatus.Done)
                {
                    await _inventoryServices.ReverseSalesOrderStockAsync(salesOrder.Id, user, ct);
                }

                // 2. Set new status
                salesOrder.Status = req.Status;

                // 3. Transitioning INTO Done -> Apply effects
                if (req.Status == SalesOrderStatus.Done)
                {
                    // For historical orders, we update status but DO NOT process stock immediately
                    // Stock must be explicitly activated unless we decide that changing status to Done implicitly activates it.
                    // Requirement: "Stock adjustments only occur when the order's status is changed ... to a stock-impacting state"
                    // This implies implicit activation on status change.
                    
                    if (salesOrder.IsHistorical)
                    {
                        if (!salesOrder.IsStockProcessed) 
                        {
                            await _inventoryServices.ProcessSalesOrderStockAsync(salesOrder.Id, user, salesOrder.OrderDate, ct);
                            salesOrder.IsStockProcessed = true;
                        }
                    }
                    else
                    {
                        await _inventoryServices.ProcessSalesOrderStockAsync(salesOrder.Id, user, timestamp, ct);
                    }
                }

                await _db.SaveChangesAsync(ct);

                // AUDIT LOG: Record the status update

                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                if (ex is ValidationException or NotFoundException) throw;
                throw new ConflictException("Could not update order status due to a database conflict.", ex);
            }
        }

        
        public async Task UpdateDueDateAsync(long orderId, DateTimeOffset newDate, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            var salesOrder = await _db.SalesOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (salesOrder.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify the due date of a cancelled order.");

            if (salesOrder.DueDate == newDate) return;

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousDate = salesOrder.DueDate;
                salesOrder.DueDate = newDate;

                await _db.SaveChangesAsync(ct);

                await transaction.CommitAsync(ct);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update due date.", ex);
            }
        }



        public async Task ActivateStockAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            
            var order = await _db.SalesOrders.FirstOrDefaultAsync(o => o.Id == orderId, ct);
            if (order == null) throw new NotFoundException($"Sales Order {orderId} not found.");

            if (!order.IsHistorical)
                throw new ValidationException("Only historical orders can be manually activated.");

            if (order.IsStockProcessed)
                throw new ValidationException("Stock has already been processed for this order.");

            if (order.Status != SalesOrderStatus.Done)
                throw new ValidationException("Order must be in 'Done' status to activate stock.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                // Process stock using the Order Date as the timestamp for the transaction
                await _inventoryServices.ProcessSalesOrderStockAsync(order.Id, user, order.OrderDate, ct);
                
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

        /// <summary>
        /// Sets or updates the Invoice PDF attachment path for an existing sales order.
        /// The web/UI layer should save the actual PDF file and pass the resolved path here.
        /// </summary>
        public async Task AttachInvoiceAsync(long orderId, string invoicePath, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");
            if (string.IsNullOrWhiteSpace(invoicePath)) throw new ValidationException("Invoice path must be provided.");

            var salesOrder = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (salesOrder.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot attach an invoice to a cancelled order.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousInvoicePath = salesOrder.InvoicePath;
                salesOrder.InvoicePath = invoicePath;
                salesOrder.InvoiceUploadedUtc = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not attach Invoice to sales order due to a database conflict.", ex);
            }
        }

        public async Task RemoveInvoiceAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            var salesOrder = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (salesOrder.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify documents for a cancelled order.");

            if (salesOrder.InvoicePath == null)
                return; // Already removed

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousInvoicePath = salesOrder.InvoicePath;
                salesOrder.InvoicePath = null;
                salesOrder.InvoiceUploadedUtc = null;

                await _db.SaveChangesAsync(ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not remove Invoice from sales order due to a database conflict.", ex);
            }
        }

        public async Task AttachReceiptAsync(long orderId, string receiptPath, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");
            if (string.IsNullOrWhiteSpace(receiptPath)) throw new ValidationException("Receipt path must be provided.");

            var salesOrder = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (salesOrder.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot attach a receipt to a cancelled order.");

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousReceiptPath = salesOrder.ReceiptPath;
                salesOrder.ReceiptPath = receiptPath;
                salesOrder.ReceiptUploadedUtc = DateTimeOffset.UtcNow;

                await _db.SaveChangesAsync(ct);


                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not attach Receipt to sales order due to a database conflict.", ex);
            }
        }

        public async Task RemoveReceiptAsync(long orderId, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");

            var salesOrder = await _db.SalesOrders
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (salesOrder.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify documents for a cancelled order.");

            if (salesOrder.ReceiptPath == null)
                return; // Already removed

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var previousReceiptPath = salesOrder.ReceiptPath;
                salesOrder.ReceiptPath = null;
                salesOrder.ReceiptUploadedUtc = null;

                await _db.SaveChangesAsync(ct);

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not remove Receipt from sales order due to a database conflict.", ex);
            }
        }

        public async Task RefundAsync(RefundSalesOrderRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);
            if (req is null) throw new ArgumentNullException(nameof(req));


            // 1. Load Order with Payments for accurate status recalculation
            var order = await _db.SalesOrders
                .Include(o => o.Lines)
                .Include(o => o.Payments)
                .FirstOrDefaultAsync(o => o.Id == req.OrderId, ct);
            
            if (order == null) throw new NotFoundException($"Sales order {req.OrderId} not found.");

            if (order.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot process refunds for a cancelled order.");

            // 2. Validate Refund Input
            bool hasAmount = req.Amount > 0;
            bool hasLines = req.LineItems != null && req.LineItems.Any(l => l.Quantity > 0);

            if (!hasAmount && !hasLines)
                throw new ValidationException("You must specify either a refund amount or products to return.");

            if (req.Amount < 0)
                 throw new ValidationException("Refund amount cannot be negative.");

            // 3. Independent Eligibility Validation (Stock vs Money)
            // Stock refund requires order to be Done
            if (hasLines && order.Status != SalesOrderStatus.Done)
                throw new ValidationException("Cannot refund stock before order is completed.");

            // Money refund: allowed when net paid > 0 (ledger-based). PaymentStatus is descriptive, not a gate.
            // Block only when there is nothing to refund.
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
                
                // 4. Create Refund Transaction
                var refundTx = new RefundTransaction
                {
                    Type = RefundType.SalesOrder,
                    SalesOrderId = order.Id,
                    Amount = req.Amount,
                    Reason = req.Reason,
                    ProcessedUtc = DateTimeOffset.UtcNow,
                    ProcessedByUserId = user.UserId,
                    ProcessedByUserDisplayName = user.UserDisplayName,
                    Note = req.Reason
                };

                // 5. Line Item Processing (Product Refund)
                if (req.LineItems != null && req.LineItems.Any())
                {
                    foreach (var refundItem in req.LineItems)
                    {
                        var line = order.Lines.FirstOrDefault(l => l.Id == refundItem.SalesOrderLineId);
                        if (line == null) 
                            throw new NotFoundException($"Sales Order Line {refundItem.SalesOrderLineId} not found in Order {order.Id}.");

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
                            SalesOrderLineId = line.Id,
                            ProductId = line.ProductId,
                            ProductNameSnapshot = line.ProductNameSnapshot,
                            Quantity = refundItem.Quantity,
                            BatchNumber = refundItem.BatchNumber ?? line.BatchNumber,
                            ProductBatchId = refundItem.ProductBatchId ?? line.ProductBatchId,
                            UnitPriceSnapshot = line.UnitPrice,
                            // Estimate line refund amount derived from quantity * unit price?
                            // Or leave it 0 if not explicitly tracked per line financially.
                            // We will store value for reference.
                            LineRefundAmount = refundItem.Quantity * line.UnitPrice 
                        });
                    }

                    // Process Stock Return
                    await _inventoryServices.RefundSalesOrderStockAsync(order.Id, req.LineItems, user, ct);
                }

                _db.RefundTransactions.Add(refundTx);

                // Create PaymentRecord for Refund
                var refundPayment = new PaymentRecord
                {
                    OrderType = OrderType.SalesOrder,
                    SalesOrderId = order.Id,
                    Amount = req.Amount,
                    PaymentDate = DateTimeOffset.UtcNow,
                    PaymentMethod = order.PaymentMethod, // Use original payment method or specific one? Cash is safest default for refunds.
                    PaymentType = PaymentRecordType.Refund,
                    Note = $"Refund for Order {order.OrderNumber}. Reason: {req.Reason}",
                    CreatedByUserId = user.UserId
                };

                order.Payments.Add(refundPayment);

                // 6. Update Order Totals
                order.RefundedAmount += req.Amount;
                
                // Recalculate status (it might downgrade from Paid to partiallyPaid)
                order.RecalculatePaymentStatus();

                // 7. Process Financial Refund - Requirement 1
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

        public async Task UpdatePaymentInfoAsync(long orderId, UpdateSalesOrderPaymentRequest req, UserContext user, CancellationToken ct = default)
        {
            ValidateUser(user);

            if (orderId <= 0) throw new ArgumentOutOfRangeException(nameof(orderId), "Order ID must be positive.");
            if (req is null) throw new ArgumentNullException(nameof(req));
            if (req.OrderId != orderId) throw new ValidationException("Order ID mismatch.");

            var salesOrder = await _db.SalesOrders
                .Include(o => o.Lines)
                .FirstOrDefaultAsync(o => o.Id == orderId, ct);

            if (salesOrder is null)
                throw new NotFoundException($"Sales order id {orderId} was not found.");

            if (salesOrder.Status == SalesOrderStatus.Cancelled)
                throw new ValidationException("Cannot modify payment info for a cancelled order.");

            // Validate logic for checks
            if (salesOrder.PaymentMethod == PaymentMethod.Check)
            {
                if (req.CheckReceived == true && !req.CheckReceivedDate.HasValue)
                    throw new ValidationException("Check received date is required when marking check as received.");
                
                if (req.CheckCashed == true && !req.CheckCashedDate.HasValue)
                    throw new ValidationException("Check cashed date is required when marking check as cashed.");
            }

            if (salesOrder.PaymentMethod == PaymentMethod.BankTransfer || req.PaymentMethod == PaymentMethod.BankTransfer)
            {
                // If it's already paid or becoming paid, we might want to ensure TransferId exists,
                // but since we are just updating metadata, we only check if they are trying to clear it while it's paid.
                if (salesOrder.PaymentStatus == PaymentStatus.Paid && string.IsNullOrWhiteSpace(req.TransferId) && string.IsNullOrWhiteSpace(salesOrder.TransferId))
                    throw new ValidationException("Transfer ID is required for bank transfer orders.");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                var beforeState = new 
                { 
                    salesOrder.PaymentStatus, 
                    salesOrder.CheckReceived, 
                    salesOrder.CheckReceivedDate,
                    salesOrder.CheckCashed,
                    salesOrder.CheckCashedDate,
                    salesOrder.TransferId,
                    salesOrder.Note
                };

                if (req.PaymentMethod.HasValue)
                {
                    salesOrder.PaymentMethod = req.PaymentMethod.Value;
                }

                if (salesOrder.PaymentMethod == PaymentMethod.Check)
                {
                    salesOrder.CheckReceived = req.CheckReceived;
                    salesOrder.CheckReceivedDate = req.CheckReceivedDate;
                    salesOrder.CheckCashed = req.CheckCashed;
                    salesOrder.CheckCashedDate = req.CheckCashedDate;
                }
                else if (salesOrder.PaymentMethod == PaymentMethod.BankTransfer)
                {
                    salesOrder.TransferId = req.TransferId;
                }

                if (!string.IsNullOrWhiteSpace(req.Note))
                {
                    salesOrder.Note = req.Note;
                }

                await _db.SaveChangesAsync(ct);


                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                await transaction.RollbackAsync(ct);
                throw new ConflictException("Could not update payment info due to a database conflict.", ex);
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
