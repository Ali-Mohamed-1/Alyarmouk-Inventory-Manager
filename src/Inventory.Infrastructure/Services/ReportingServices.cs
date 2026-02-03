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

            // Get total on-hand stock across all products
            var totalOnHand = await _db.StockSnapshots
                .AsNoTracking()
                .SumAsync(s => (decimal?)s.OnHand, ct) ?? 0m;

            // Get low stock count (products where OnHand <= ReorderPoint and product is active)
            var lowStockCount = await (from p in _db.Products
                                       join s in _db.StockSnapshots on p.Id equals s.ProductId into ps
                                       from snapshot in ps.DefaultIfEmpty()
                                       where p.IsActive && (snapshot == null || snapshot.OnHand <= p.ReorderPoint)
                                       select p)
                .AsNoTracking()
                .CountAsync(ct);

            // Get stock by category
            var stockByCategory = await (from p in _db.Products
                                         join c in _db.categories on p.CategoryId equals c.Id
                                         join s in _db.StockSnapshots on p.Id equals s.ProductId into ps
                                         from snapshot in ps.DefaultIfEmpty()
                                         where p.IsActive
                                         group new { snapshot, c } by new { c.Id, c.Name } into g
                                         select new DashboardStockByCategoryPointDto
                                         {
                                             CategoryName = g.Key.Name,
                                             OnHand = g.Sum(x => x.snapshot != null ? x.snapshot.OnHand : 0m)
                                         })
                .AsNoTracking()
                .OrderBy(x => x.CategoryName)
                .ToListAsync(ct);

            return new DashboardResponseDto
            {
                TotalProducts = totalProducts,
                TotalOnHand = totalOnHand,
                LowStockCount = lowStockCount,
                StockByCategory = stockByCategory
            };
        }

        public async Task<IReadOnlyList<LowStockItemResponseDto>> GetLowStockAsync(CancellationToken ct = default)
        {
            return await (from p in _db.Products
                         join c in _db.categories on p.CategoryId equals c.Id
                         join s in _db.StockSnapshots on p.Id equals s.ProductId into ps
                         from snapshot in ps.DefaultIfEmpty()
                         where p.IsActive && (snapshot == null || snapshot.OnHand <= p.ReorderPoint)
                         orderby snapshot != null ? snapshot.OnHand : 0m ascending, p.Name ascending
                         select new LowStockItemResponseDto
                         {
                             ProductId = p.Id,
                             ProductName = p.Name,
                             CategoryName = c.Name,
                             OnHand = snapshot != null ? snapshot.OnHand : 0m,
                             Unit = p.Unit,
                             ReorderPoint = p.ReorderPoint
                         })
                .AsNoTracking()
                .ToListAsync(ct);
        }

        public async Task<SupplierBalanceResponseDto> GetSupplierBalanceAsync(int supplierId, CancellationToken ct = default)
        {
            var supplier = await _db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == supplierId, ct);
            if (supplier is null) throw new NotFoundException($"Supplier {supplierId} not found.");

            var totalOrders = await _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.SupplierId == supplierId && po.Status != PurchaseOrderStatus.Cancelled)
                .SumAsync(po => (decimal?)po.TotalAmount - (decimal?)po.RefundedAmount, ct) ?? 0m;

            var totalPayments = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Expense &&
                            t.SupplierId == supplierId)
                .SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

            var asOf = DateTimeOffset.UtcNow;

            // Query for unpaid/partially-paid purchase orders
            var pendingQuery = _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.SupplierId == supplierId && 
                            po.Status != PurchaseOrderStatus.Cancelled &&
                            (po.PaymentStatus == PurchasePaymentStatus.Unpaid || po.PaymentStatus == PurchasePaymentStatus.PartiallyPaid));

            // Total Pending: sum of all unpaid/partially-paid orders
            var totalPending = await pendingQuery.SumAsync(po => (decimal?)po.TotalAmount - (decimal?)po.RefundedAmount, ct) ?? 0m;

            // Deserved: subset where payment deadline has passed (overdue)
            var deserved = await pendingQuery
                .Where(po => po.PaymentDeadline.HasValue && po.PaymentDeadline.Value < asOf)
                .SumAsync(po => (decimal?)po.TotalAmount - (decimal?)po.RefundedAmount, ct) ?? 0m;

            return new SupplierBalanceResponseDto
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.Name,
                TotalOrders = totalOrders,
                TotalPayments = totalPayments,
                TotalPending = totalPending,
                Deserved = deserved,
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
                           (o.PaymentStatus == PaymentStatus.Pending || o.PaymentStatus == PaymentStatus.PartiallyPaid));

            // Total Pending: sum of all unpaid/partially-paid orders
            var totalPending = await query.SumAsync(o => o.TotalAmount - o.RefundedAmount, ct);

            // Deserved: subset where due date has passed (overdue)
            var deserved = await query
                .Where(o => o.DueDate < asOf)
                .SumAsync(o => o.TotalAmount - o.RefundedAmount, ct);

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

            // 2. Sales Refunds (Aggregated by period)
            var salesRefundsTotal = await _db.RefundTransactions
                .AsNoTracking()
                .Where(r => r.Type == RefundType.SalesOrder &&
                            r.ProcessedUtc >= fromUtc &&
                            r.ProcessedUtc <= toUtc)
                .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

            // Refund Base = Sum / (1 + VAT - ManTax)
            decimal refundBaseDivisor = 1m + TaxConstants.VatRate - TaxConstants.ManufacturingTaxRate;
            decimal refundBase = salesRefundsTotal / (refundBaseDivisor > 0 ? refundBaseDivisor : 1.13m);
            
            // Adjust Sales Metrics for Refunds
            var net_sales_subtotal = Math.Max(0, gross_sales_subtotal - refundBase);
            sales_vat = Math.Max(0, sales_vat - (refundBase * TaxConstants.VatRate));
            sales_man_tax = Math.Max(0, sales_man_tax - (refundBase * TaxConstants.ManufacturingTaxRate));

            // 3. Purchase Side (Taxes + Receipt Expenses)
            var purchaseTransactions = await (from t in _db.FinancialTransactions
                                              join po in _db.PurchaseOrders on t.PurchaseOrderId equals po.Id
                                              where t.Type == FinancialTransactionType.Expense &&
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

            var purchaseRefundsTotal = await _db.RefundTransactions
                .AsNoTracking()
                .Where(r => r.Type == RefundType.PurchaseOrder &&
                            r.ProcessedUtc >= fromUtc &&
                            r.ProcessedUtc <= toUtc)
                .SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

            decimal pRefBase = purchaseRefundsTotal / refundBaseDivisor;
            purchase_vat = Math.Max(0, purchase_vat - (pRefBase * TaxConstants.VatRate));
            purchase_man_tax = Math.Max(0, purchase_man_tax - (pRefBase * TaxConstants.ManufacturingTaxRate));

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

            // Purchase Refunds also reduce effective COGS of bought items if they were part of it
            // but functionally, COGS is what was GONE. If it was returned, it didn't generate revenue.
            // Simplified: subtract the Base of returned purchases from total inventory outflow.
            var net_cogs = Math.Max(0, calculatedCogs - pRefBase);

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

            var net_profit = operating_profit - net_vat - net_man_tax;

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
                ProfitMargin = net_sales_subtotal > 0 ? Math.Round((net_profit / net_sales_subtotal) * 100, 2) : 0m
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
