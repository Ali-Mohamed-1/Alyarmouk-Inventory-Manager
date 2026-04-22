using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs.Reporting;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs;
using Inventory.Domain.Entities;
using Inventory.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Inventory.Application.Exceptions;

using Inventory.Domain.Constants;

namespace Inventory.Infrastructure.Services
{
    public sealed class ReportingServices : IReportingServices
    {
        private readonly AppDbContext _db;

        public ReportingServices(AppDbContext db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public async Task<DashboardResponseDto> GetDashboardAsync(CancellationToken ct = default)
        {
            // Get total products count
            var totalProducts = await _db.Products
                .AsNoTracking()
                .CountAsync(ct);

            // Get total on-hand stock across all products (derived from Transactions now)
            var totalOnHand = await _db.InventoryTransactions
                .AsNoTracking()
                .SumAsync(t => (decimal?)t.QuantityDelta, ct) ?? 0m;

            // Get low stock count (products where Sum(Batch.OnHand) <= ReorderPoint and product is active)
            // We need to group batches by product first
            var lowStockCount = await _db.Products
                .AsNoTracking()
                .Where(p => p.IsActive)
                .Select(p => new
                {
                    p.Id,
                    p.ReorderPoint,
                    TotalOnHand = _db.InventoryTransactions.Where(t => t.ProductId == p.Id).Sum(t => (decimal?)t.QuantityDelta) ?? 0m
                })
                .Where(x => x.TotalOnHand <= x.ReorderPoint)
                .CountAsync(ct);


            // Get total sales and purchase orders count
            var totalSalesOrders = await _db.SalesOrders.CountAsync(ct);
            var totalPurchaseOrders = await _db.PurchaseOrders.CountAsync(ct);

            return new DashboardResponseDto
            {
                TotalProducts = totalProducts,
                TotalOnHand = totalOnHand,
                LowStockCount = lowStockCount,
                TotalSalesOrders = totalSalesOrders,
                TotalPurchaseOrders = totalPurchaseOrders
            };
        }

        public async Task<IReadOnlyList<LowStockItemResponseDto>> GetLowStockAsync(CancellationToken ct = default)
        {
            // Similar logic: Active products where Total Batch OnHand <= ReorderPoint
            return await _db.Products
                .AsNoTracking()
                .Where(p => p.IsActive)
                .Select(p => new LowStockItemResponseDto
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    OnHand = _db.InventoryTransactions.Where(t => t.ProductId == p.Id).Sum(t => (decimal?)t.QuantityDelta) ?? 0m,
                    Unit = p.Unit,
                    ReorderPoint = p.ReorderPoint
                })
                .ToListAsync(ct);
        }

        public async Task<SupplierBalanceResponseDto> GetSupplierBalanceAsync(int supplierId, CancellationToken ct = default)
        {
            var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
            if (supplier is null) throw new NotFoundException($"Supplier {supplierId} not found.");

            var asOf = DateTimeOffset.UtcNow;

            // ── Metric 1: Total Volume ─────────────────────────────────────────────
            // Sum of ALL PurchaseOrder TotalAmounts, regardless of status.
            // Historical view — how much we ever ordered from this supplier.
            var totalVolume = await _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.SupplierId == supplierId)
                .SumAsync(po => (decimal?)po.EffectiveTotal, ct) ?? 0m;

            // ── Metric 2: NetOwedToSupplier (Pending) ──────────────────────────────
            //
            // Formula: sum(PO.Remaining for non-Cancelled POs)
            //        - sum(SSO.TotalAmount where Status != Cancelled)
            //
            // A SupplierSalesOrder is a permanent bookkeeping entry. An Active SSO
            // immediately and permanently reduces NetOwedToSupplier by its full
            // TotalAmount, regardless of PaymentStatus. A Cancelled SSO has zero effect.
            //
            // Step A: Compute remaining amount per non-Cancelled PO.
            var poData = await _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.SupplierId == supplierId &&
                             po.Status != PurchaseOrderStatus.Cancelled)
                .Select(po => new
                {
                    po.EffectiveTotal,
                    po.PaymentStatus,
                    po.DueDate,
                    NetPaid = (po.Payments
                                   .Where(p => p.PaymentType == PaymentRecordType.Payment)
                                   .Sum(p => (decimal?)p.Amount) ?? 0m)
                             - (po.Payments
                                   .Where(p => p.PaymentType == PaymentRecordType.Refund)
                                   .Sum(p => (decimal?)p.Amount) ?? 0m)
                })
                .ToListAsync(ct);

            // Remaining per PO = max(0, EffectiveTotal - NetPaid)
            var totalPoRemaining = poData.Sum(po => Math.Max(0m, po.EffectiveTotal - po.NetPaid));

            // Step B: Sum all Active (non-Cancelled) SSO amounts.
            // These are permanent debt reductions — PaymentStatus is irrelevant.
            var totalActiveSsoAmount = await _db.SupplierSalesOrders
                .AsNoTracking()
                .Where(sso => sso.SupplierId == supplierId &&
                              sso.Status != SalesOrderStatus.Cancelled)
                .SumAsync(sso => (decimal?)sso.TotalAmount, ct) ?? 0m; // SSO TotalAmount is its contract value

            // NetOwedToSupplier: clamped to 0 minimum (can't have negative owed)
            var netOwedToSupplier = Math.Max(0m, totalPoRemaining - totalActiveSsoAmount);

            // ── Metric 3: Paid ─────────────────────────────────────────────────────
            // Total cash paid OUT to this supplier from the PaymentRecord ledger,
            // net of any refunds received from the supplier.
            // Source: PaymentRecords linked to this supplier's PurchaseOrders only.
            // SupplierSalesOrders NEVER contribute to Paid — they are bookkeeping entries,
            // not cash transactions.
            var supplierPoIds = await _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.SupplierId == supplierId)
                .Select(po => po.Id)
                .ToListAsync(ct);

            var totalCashOut = await _db.PaymentRecords
                .AsNoTracking()
                .Where(p => p.OrderType == OrderType.PurchaseOrder &&
                            p.PurchaseOrderId.HasValue &&
                            supplierPoIds.Contains(p.PurchaseOrderId!.Value) &&
                            p.PaymentType == PaymentRecordType.Payment)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var totalRefundsReceived = await _db.PaymentRecords
                .AsNoTracking()
                .Where(p => p.OrderType == OrderType.PurchaseOrder &&
                            p.PurchaseOrderId.HasValue &&
                            supplierPoIds.Contains(p.PurchaseOrderId!.Value) &&
                            p.PaymentType == PaymentRecordType.Refund)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var paid = Math.Max(0m, totalCashOut - totalRefundsReceived);

            // ── Metric 4: Overdue ──────────────────────────────────────────────────
            // Remaining balance on PurchaseOrders where:
            //   - DueDate < now
            //   - Status != Cancelled
            //   - PaymentStatus != Paid
            // SupplierSalesOrders do NOT offset the Overdue metric.
            // Overdue reflects raw cash obligations that are past due.
            var overduePoData = await _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.SupplierId == supplierId &&
                             po.Status != PurchaseOrderStatus.Cancelled &&
                             po.PaymentStatus != PurchasePaymentStatus.Paid &&
                             po.DueDate.HasValue &&
                             po.DueDate.Value < asOf)
                .Select(po => new
                {
                    po.EffectiveTotal,
                    NetPaid = (po.Payments
                                   .Where(p => p.PaymentType == PaymentRecordType.Payment)
                                   .Sum(p => (decimal?)p.Amount) ?? 0m)
                             - (po.Payments
                                   .Where(p => p.PaymentType == PaymentRecordType.Refund)
                                   .Sum(p => (decimal?)p.Amount) ?? 0m)
                })
                .ToListAsync(ct);

            var overdue = overduePoData.Sum(po => Math.Max(0m, po.EffectiveTotal - po.NetPaid));

            return new SupplierBalanceResponseDto
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.Name,
                TotalVolume = totalVolume,
                NetOwedToSupplier = netOwedToSupplier,
                Paid = paid,
                Overdue = overdue,
                AsOfUtc = asOf
            };
        }

        public async Task<CustomerBalanceResponseDto> GetCustomerBalanceAsync(int customerId, DateTimeOffset? asOfUtc = null, CancellationToken ct = default)
        {
            if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId), "Customer ID must be positive.");

            var asOf = asOfUtc ?? DateTimeOffset.UtcNow;

            // Consider orders that are unpaid OR partially paid (not fully paid)
            var query = _db.SalesOrders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId && 
                           o.Status != SalesOrderStatus.Cancelled &&
                           (o.PaymentStatus == PaymentStatus.Pending || o.PaymentStatus == PaymentStatus.PartiallyPaid));

            // Total Pending: sum of all unpaid/partially-paid orders remaining balances
            var totalPending = await query.SumAsync(o => o.EffectiveTotal - (o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount)), ct);

            // Deserved: subset where due date has passed (overdue)
            var deserved = await query
                .Where(o => o.DueDate < asOf)
                .SumAsync(o => o.EffectiveTotal - (o.Payments.Where(p => p.PaymentType == PaymentRecordType.Payment).Sum(p => p.Amount) - o.Payments.Where(p => p.PaymentType == PaymentRecordType.Refund).Sum(p => p.Amount)), ct);

            return new CustomerBalanceResponseDto
            {
                CustomerId = customerId,
                TotalPending = totalPending,
                Deserved = deserved,
                AsOfUtc = asOf
            };
        }

        public async Task<FinancialSummaryResponseDto> GetFinancialSummaryAsync(FinancialReportFilterDto filter, CancellationToken ct = default)
        {
            if (filter is null) throw new ArgumentNullException(nameof(filter));

            var (fromUtc, toUtc) = ResolveDateRange(filter);

            // author:gross_sales_sub_total (before refunds)
            // author:refund_sub_total (portion of refunds that is the base price)
            
            // 1. Sales Side (Revenue + Taxes)
            // Join FinancialTransactions (Revenue) with SalesOrders to get accurate tax data per transaction
            var salesTransactions = await (from t in _db.FinancialTransactions
                                           join so in _db.SalesOrders on t.SalesOrderId equals so.Id
                                           where t.Type == FinancialTransactionType.Revenue &&
                                                 so.Status != SalesOrderStatus.Cancelled &&
                                                 t.TimestampUtc >= fromUtc &&
                                                 t.TimestampUtc <= toUtc
                                           select new
                                           {
                                               TxAmount = t.Amount,
                                               OrderTotal = so.TotalAmount,
                                               OrderSubtotal = so.Subtotal,
                                               OrderVat = so.VatAmount,
                                               OrderManTax = so.ManufacturingTaxAmount,
                                               OrderId = so.Id
                                           }).AsNoTracking().ToListAsync(ct);

            var gross_sales_subtotal = 0m;
            var sales_vat = 0m;
            var sales_man_tax = 0m;
            var revenueTransactionsWithOrder = new List<(long SalesOrderId, decimal Amount, decimal TotalAmount)>();

            foreach (var item in salesTransactions)
            {
                if (item.OrderTotal <= 0) continue;
                var ratio = item.TxAmount / item.OrderTotal;

                gross_sales_subtotal += item.OrderSubtotal * ratio;
                sales_vat += item.OrderVat * ratio;
                sales_man_tax += item.OrderManTax * ratio;
                
                revenueTransactionsWithOrder.Add((item.OrderId, item.TxAmount, item.OrderTotal));
            }

            // 2. Sales Refunds (Granular Line-Item Impact)
            var refundLines = await (from rl in _db.RefundTransactionLines
                                     join r in _db.RefundTransactions on rl.RefundTransactionId equals r.Id
                                     where r.Type == RefundType.SalesOrder &&
                                           r.ProcessedUtc >= fromUtc &&
                                           r.ProcessedUtc <= toUtc
                                     select new { rl.SubtotalRefunded, rl.VatRefunded, rl.ManTaxRefunded })
                                    .AsNoTracking()
                                    .ToListAsync(ct);

            var refundSubtotal = refundLines.Sum(l => l.SubtotalRefunded);
            var refundVat = refundLines.Sum(l => l.VatRefunded);
            var refundManTax = refundLines.Sum(l => l.ManTaxRefunded);

            // 2.1 Handle Monetary-only refunds (where no lines exist)
            // We find the difference between RefundTransaction.Amount and Sum(Lines.LineRefundAmount)
            var salesRefundsTotal = await _db.RefundTransactions
                .AsNoTracking()
                .Where(r => r.Type == RefundType.SalesOrder &&
                            r.ProcessedUtc >= fromUtc &&
                            r.ProcessedUtc <= toUtc)
                .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

            decimal totalLineRefunds = await (from rl in _db.RefundTransactionLines
                                              join r in _db.RefundTransactions on rl.RefundTransactionId equals r.Id
                                              where r.Type == RefundType.SalesOrder &&
                                                    r.ProcessedUtc >= fromUtc &&
                                                    r.ProcessedUtc <= toUtc
                                              select rl.LineRefundAmount)
                                             .SumAsync(ct);

            decimal monetaryOnlyRefund = Math.Max(0, salesRefundsTotal - totalLineRefunds);
            if (monetaryOnlyRefund > 0)
            {
                // Fallback to estimated breakdown for monetary-only refunds
                decimal divisor = 1m + TaxConstants.VatRate - TaxConstants.ManufacturingTaxRate;
                decimal mBase = monetaryOnlyRefund / (divisor > 0 ? divisor : 1.13m);
                refundSubtotal += mBase;
                refundVat += mBase * TaxConstants.VatRate;
                refundManTax += mBase * TaxConstants.ManufacturingTaxRate;
            }
            
            // Adjust Sales Metrics for Refunds
            var net_sales_subtotal = Math.Max(0m, gross_sales_subtotal - refundSubtotal);
            sales_vat = Math.Max(0m, sales_vat - refundVat);
            sales_man_tax = Math.Max(0m, sales_man_tax - refundManTax);

            // 3. Purchase Side (Taxes + Receipt Expenses)
            var purchaseTransactions = await (from t in _db.FinancialTransactions
                                              join po in _db.PurchaseOrders on t.PurchaseOrderId equals po.Id
                                              where t.Type == FinancialTransactionType.Expense &&
                                                    po.Status != PurchaseOrderStatus.Cancelled &&
                                                    !t.IsInternalExpense &&
                                                    t.TimestampUtc >= fromUtc &&
                                                    t.TimestampUtc <= toUtc
                                              select new
                                              {
                                                  TxAmount = t.Amount,
                                                  OrderTotal = po.TotalAmount,
                                                  OrderVat = po.VatAmount,
                                                  OrderManTax = po.ManufacturingTaxAmount,
                                                  OrderExpenses = po.ReceiptExpenses
                                              }).AsNoTracking().ToListAsync(ct);

            var purchase_vat = 0m;
            var purchase_man_tax = 0m;
            var receipt_expenses = 0m;

            foreach (var item in purchaseTransactions)
            {
                if (item.OrderTotal <= 0) continue;
                var ratio = item.TxAmount / item.OrderTotal;

                purchase_vat += item.OrderVat * ratio;
                purchase_man_tax += item.OrderManTax * ratio;
                receipt_expenses += item.OrderExpenses * ratio;
            }

            // 3.1 Purchase Refunds
            var pRefundLines = await (from rl in _db.RefundTransactionLines
                                      join r in _db.RefundTransactions on rl.RefundTransactionId equals r.Id
                                      where r.Type == RefundType.PurchaseOrder &&
                                            r.ProcessedUtc >= fromUtc &&
                                            r.ProcessedUtc <= toUtc
                                      select new { rl.SubtotalRefunded, rl.VatRefunded, rl.ManTaxRefunded })
                                     .AsNoTracking()
                                     .ToListAsync(ct);

            var pRefundSubtotal = pRefundLines.Sum(l => l.SubtotalRefunded);
            var pRefundVat = pRefundLines.Sum(l => l.VatRefunded);
            var pRefundManTax = pRefundLines.Sum(l => l.ManTaxRefunded);

            var purchaseRefundsTotal = await _db.RefundTransactions
                .AsNoTracking()
                .Where(r => r.Type == RefundType.PurchaseOrder &&
                            r.ProcessedUtc >= fromUtc &&
                            r.ProcessedUtc <= toUtc)
                .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

            decimal pTotalLineRefunds = await (from rl in _db.RefundTransactionLines
                                               join r in _db.RefundTransactions on rl.RefundTransactionId equals r.Id
                                               where r.Type == RefundType.PurchaseOrder &&
                                                     r.ProcessedUtc >= fromUtc &&
                                                     r.ProcessedUtc <= toUtc
                                               select rl.LineRefundAmount)
                                              .SumAsync(ct);

            decimal pMonetaryOnlyRefund = Math.Max(0m, purchaseRefundsTotal - pTotalLineRefunds);
            if (pMonetaryOnlyRefund > 0)
            {
                decimal divisor = 1m + TaxConstants.VatRate - TaxConstants.ManufacturingTaxRate;
                decimal mBase = pMonetaryOnlyRefund / divisor;
                pRefundSubtotal += mBase;
                pRefundVat += mBase * TaxConstants.VatRate;
                pRefundManTax += mBase * TaxConstants.ManufacturingTaxRate;
            }

            purchase_vat = Math.Max(0m, purchase_vat - pRefundVat);
            purchase_man_tax = Math.Max(0m, purchase_man_tax - pRefundManTax);

            // COGS Calculation
            var salesOrderIds = revenueTransactionsWithOrder.Select(x => x.SalesOrderId).Distinct().ToList();
            var salesOrderLines = await _db.SalesOrderLines
                .AsNoTracking()
                .Where(l => salesOrderIds.Contains(l.SalesOrderId))
                .Include(l => l.ProductBatch)
                .ToListAsync(ct);

            var salesOrderLinesMap = salesOrderLines.GroupBy(l => l.SalesOrderId).ToDictionary(g => g.Key, g => g.ToList());

            var calculatedCogs = 0m;
            foreach (var t in revenueTransactionsWithOrder)
            {
                if (!salesOrderLinesMap.TryGetValue(t.SalesOrderId, out var lines)) continue;
                var totalOrderCost = lines.Sum(l => l.Quantity * (l.ProductBatch?.UnitCost ?? 0m));
                var ratio = t.Amount / t.TotalAmount;
                calculatedCogs += totalOrderCost * ratio;
            }

            // Net COGS = Gross COGS of sold items - COGS of returned items
            var returnedCogs = await (from rl in _db.RefundTransactionLines
                                      join r in _db.RefundTransactions on rl.RefundTransactionId equals r.Id
                                      join sol in _db.SalesOrderLines on rl.SalesOrderLineId equals sol.Id
                                      where r.Type == RefundType.SalesOrder &&
                                            r.ProcessedUtc >= fromUtc &&
                                            r.ProcessedUtc <= toUtc
                                      select rl.Quantity * (sol.ProductBatch != null ? sol.ProductBatch.UnitCost : 0m))
                                     .SumAsync(ct) ?? 0m;

            var net_cogs = Math.Max(0m, calculatedCogs - returnedCogs);

            // CALCULATED FLOW
            var gross_profit = net_sales_subtotal - net_cogs;

            var internalExpenses = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Expense &&
                            t.IsInternalExpense &&
                            t.TimestampUtc >= fromUtc &&
                            t.TimestampUtc <= toUtc)
                .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

            var total_internal_expenses = internalExpenses + receipt_expenses;
            var operating_profit = gross_profit - total_internal_expenses;

            var net_vat = sales_vat - purchase_vat;
            var net_man_tax = sales_man_tax - purchase_man_tax;

            var net_profit = operating_profit; // Do not subtract taxes from P&L Net Profit

            // Purchase Payments (Period)
            var purchasePaymentsTotal = purchaseTransactions.Sum(p => p.TxAmount);

            // Bank Balance (Global / Running Total)
            // Strict Cash Basis from PaymentRecord and internal expenses
            var bankSettings = await _db.BankSystemSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            var BaseBankBalance = bankSettings?.BankBaseBalance ?? 0m;

            var totalPaid = await _db.PaymentRecords
                .AsNoTracking()
                .Where(p => p.PaymentType == PaymentRecordType.Payment && p.OrderType == OrderType.SalesOrder)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var totalRefunded = await _db.PaymentRecords
                .AsNoTracking()
                .Where(p => p.PaymentType == PaymentRecordType.Refund && p.OrderType == OrderType.SalesOrder)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var netCashReceived = totalPaid - totalRefunded;

            var supplierPaid = await _db.PaymentRecords
                .AsNoTracking()
                .Where(p => p.PaymentType == PaymentRecordType.Payment && p.OrderType == OrderType.PurchaseOrder)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;

            var supplierRefunded = await _db.PaymentRecords
                .AsNoTracking()
                .Where(p => p.PaymentType == PaymentRecordType.Refund && p.OrderType == OrderType.PurchaseOrder)
                .SumAsync(p => (decimal?)p.Amount, ct) ?? 0m;
                
            supplierPaid -= supplierRefunded; // Net cash out to suppliers

            var totalExpensesPaid = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Expense && t.IsInternalExpense)
                .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

            var bankBalance = BaseBankBalance + netCashReceived - supplierPaid - totalExpensesPaid;

            return new FinancialSummaryResponseDto
            {
                SalesRevenue = Math.Round(gross_sales_subtotal, 2), // Legacy "Before" label
                SalesProfit = Math.Round(net_sales_subtotal, 2),   // Legacy "After" label, Hero metric
                CostOfGoods = Math.Round(net_cogs, 2),
                TotalVat = Math.Round(net_vat, 2),
                TotalManufacturingTax = Math.Round(net_man_tax, 2),
                InternalExpenses = Math.Round(total_internal_expenses, 2),
                GrossProfit = Math.Round(gross_profit, 2),
                NetProfit = Math.Round(net_profit, 2),
                ProfitMargin = net_sales_subtotal > 0 ? Math.Round((net_profit / net_sales_subtotal) * 100, 2) : 0m,
                PurchasePayments = Math.Round(purchasePaymentsTotal, 2),
                BankBalance = Math.Round(bankBalance, 2)
            };
        }

        public async Task<IReadOnlyList<InternalExpenseResponseDto>> GetInternalExpensesAsync(FinancialReportFilterDto filter, CancellationToken ct = default)
        {
            if (filter is null) throw new ArgumentNullException(nameof(filter));

            var (fromUtc, toUtc) = ResolveDateRange(filter);

            var expenses = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Expense &&
                            t.IsInternalExpense &&
                            t.TimestampUtc >= fromUtc &&
                            t.TimestampUtc <= toUtc)
                .OrderBy(t => t.TimestampUtc)
                .Select(t => new InternalExpenseResponseDto
                {
                    Id = t.Id,
                    InternalExpenseType = t.InternalExpenseType ?? InternalExpenseType.Other,
                    Description = t.Note ?? string.Empty,
                    Note = null,
                    Amount = t.Amount,
                    TimestampUtc = t.TimestampUtc,
                    CreatedByUserDisplayName = t.UserDisplayName
                })
                .ToListAsync(ct);

            return expenses;
        }

        public async Task<long> CreateInternalExpenseAsync(CreateInternalExpenseRequestDto request, UserContext user, CancellationToken ct = default)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));
            if (user is null) throw new ArgumentNullException(nameof(user));
            if (request.Amount <= 0) throw new ArgumentOutOfRangeException(nameof(request.Amount), "Amount must be positive.");

            var entity = new FinancialTransaction
            {
                Type = FinancialTransactionType.Expense,
                Amount = request.Amount,
                IsInternalExpense = true,
                InternalExpenseType = request.InternalExpenseType,
                TimestampUtc = request.TimestampUtc ?? DateTimeOffset.UtcNow,
                UserId = user.UserId,
                UserDisplayName = user.UserDisplayName,
                Note = string.IsNullOrWhiteSpace(request.Note)
                    ? request.Description
                    : $"{request.Description} - {request.Note}"
            };

            await _db.FinancialTransactions.AddAsync(entity, ct);
            await _db.SaveChangesAsync(ct);

            return entity.Id;
        }

        private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ResolveDateRange(FinancialReportFilterDto filter)
        {
            var nowUtc = DateTimeOffset.UtcNow;
            
            if (filter.DateRangeType == FinancialDateRangeType.Custom &&
                filter.FromUtc.HasValue &&
                filter.ToUtc.HasValue)
            {
                return (filter.FromUtc.Value, filter.ToUtc.Value);
            }

            // Get local current time based on offset
            var localNow = nowUtc.AddMinutes(-filter.TimezoneOffsetMinutes);
            DateTimeOffset localStart;
            DateTimeOffset localEnd;

            switch (filter.DateRangeType)
            {
                case FinancialDateRangeType.Today:
                    localStart = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset);
                    localEnd = localStart.AddDays(1).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisWeek:
                    // Week Starts on Sunday (User Preference)
                    var diffDay = (int)localNow.DayOfWeek;
                    localStart = new DateTimeOffset(localNow.Year, localNow.Month, localNow.Day, 0, 0, 0, localNow.Offset).AddDays(-diffDay);
                    localEnd = localStart.AddDays(7).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisMonth:
                    localStart = new DateTimeOffset(localNow.Year, localNow.Month, 1, 0, 0, 0, localNow.Offset);
                    localEnd = localStart.AddMonths(1).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisQuarter:
                    var quarter = (localNow.Month - 1) / 3;
                    var qStartMonth = quarter * 3 + 1;
                    localStart = new DateTimeOffset(localNow.Year, qStartMonth, 1, 0, 0, 0, localNow.Offset);
                    localEnd = localStart.AddMonths(3).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisYear:
                    localStart = new DateTimeOffset(localNow.Year, 1, 1, 0, 0, 0, localNow.Offset);
                    localEnd = localStart.AddYears(1).AddTicks(-1);
                    break;
                default:
                    localStart = localNow;
                    localEnd = localNow;
                    break;
            }

            // Convert local boundaries back to UTC for DB query
            var fromUtc = localStart.ToUniversalTime();
            var toUtc = localEnd.ToUniversalTime();

            return (fromUtc, toUtc);
        }
    }
}
