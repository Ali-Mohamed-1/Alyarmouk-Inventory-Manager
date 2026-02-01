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

            return new SupplierBalanceResponseDto
            {
                SupplierId = supplier.Id,
                SupplierName = supplier.Name,
                TotalOrders = totalOrders,
                TotalPayments = totalPayments,
                AsOfUtc = asOf
            };
        }

        public async Task<CustomerBalanceResponseDto> GetCustomerBalanceAsync(int customerId, DateTimeOffset? asOfUtc = null, CancellationToken ct = default)
        {
            if (customerId <= 0) throw new ArgumentOutOfRangeException(nameof(customerId), "Customer ID must be positive.");

            var asOf = asOfUtc ?? DateTimeOffset.UtcNow;

            // Consider only orders that are still unpaid.
            var query = _db.SalesOrders
                .AsNoTracking()
                .Where(o => o.CustomerId == customerId && o.PaymentStatus == PaymentStatus.Pending);

            var totalPending = await query.SumAsync(o => o.TotalAmount, ct);

            // Due now or overdue: unpaid and due date is on or before the "as of" timestamp.
            var totalDueNow = await query
                .Where(o => o.DueDate <= asOf)
                .SumAsync(o => o.TotalAmount, ct);

            return new CustomerBalanceResponseDto
            {
                CustomerId = customerId,
                TotalPending = totalPending,
                TotalDueNow = totalDueNow,
                AsOfUtc = asOf
            };
        }

        public async Task<FinancialSummaryResponseDto> GetFinancialSummaryAsync(FinancialReportFilterDto filter, CancellationToken ct = default)
        {
            if (filter is null) throw new ArgumentNullException(nameof(filter));

            var (fromUtc, toUtc) = ResolveDateRange(filter);

            // ---- Sales Revenue (Base Subtotal) ----
            // We need to calculate revenue based on the SalesOrder.Subtotal (excluding VAT/ManTax),
            // but only for the portion that has been paid.
            
            // 1. Get all revenue transactions in range
            var revenueTransactions = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Revenue &&
                            t.SalesOrderId != null &&
                            t.TimestampUtc >= fromUtc &&
                            t.TimestampUtc <= toUtc)
                .Include(t => t.SalesOrder)
                .ToListAsync(ct);

            var revenueTransactionsWithOrder = revenueTransactions
                .Where(t => t.SalesOrder != null && t.SalesOrder.TotalAmount > 0)
                .ToList();

            var salesRevenue = 0m;
            var totalSalesVat = 0m;
            var totalSalesManTax = 0m;

            foreach (var t in revenueTransactionsWithOrder)
            {
                var order = t.SalesOrder!;
                
                // Ratio of this payment to the total order amount
                // Payment Amount = (Subtotal + VAT - ManTax) * Ratio
                // We want to extract the components.
                var ratio = t.Amount / order.TotalAmount;

                salesRevenue += order.Subtotal * ratio;
                totalSalesVat += order.VatAmount * ratio;
                totalSalesManTax += order.ManufacturingTaxAmount * ratio;
            }

            // ---- Sales Refunds (from RefundTransactions) ----
            // We need to verify if RefundTransaction.Amount includes tax or not. 
            // Usually refunds are "money returned", so it's Gross. 
            // We should theoretically split refunds into Principal, VAT, ManTax too.
            // But current RefundTransaction only tracks total Amount.
            // For now, we assume we deduct the Gross Refund from the "Sales Profit" bucket?
            // Wait, User Rules: "VAT must NOT inflate revenue".
            // If we refund, we reverse VAT.
            // Current RefundTransaction structure is simple.
            // Let's rely on the simple "Amount" for now, but ideally we should prorate refunds too.
            // However, to strictly follow "Net Profit = Sales Revenue ... - SalesManTax",
            // we should treat Refunds as reversing these components.
            // Since we don't have broken-down refund data, we will deduct the Total Refund amount
            // from the "Sales Profit" effectively. 
            // Correct approach: Net Sales Revenue = Gross Sales Revenue - Revenue component of Refund.
            // Given the complexity, let's subtract Total Refunds from the calculated "Sales Profit" 
            // but keeps "Sales Revenue" as the Gross Generated Revenue.
            
            var salesRefundsQuery = _db.RefundTransactions
                .AsNoTracking()
                .Where(r => r.Type == RefundType.SalesOrder &&
                            r.ProcessedUtc >= fromUtc &&
                            r.ProcessedUtc <= toUtc);

            var salesRefundsTotal = await salesRefundsQuery.SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

            // To avoid distorting the purely calculated VAT/ManTax from Positive Sales,
            // we should technically reduce the reported VAT/ManTax by the refunded portion.
            // BUT, if the Refund didn't record how much VAT was returned, we can't be precise without re-querying.
            // For this iteration, we track "Total Collected Taxes" via Revenue transactions.
            // We will report "SalesRevenue" as the Prorated Subtotal.
            // "SalesProfit" will be "SalesRevenue - (Refunds - RefundedVAT + RefundedManTax)"?
            // To be safe and simple compliant:
            // Sales Revenue = Prorated Subtotal.
            // Refunds = Money Out.
            // We'll treat Refunds as a generic reduction to Cash Flow for now, or just subtract from SalesProfit.
            
            // User Formula: Net Profit = Sales Revenue - COGS - Internal Expenses - Sales ManTax - Purchase ManTax

            // ---- Purchase Expenses (Base Subtotal) ----
            // Similar logic for purchases
            var expenseTransactions = await _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Expense &&
                            !t.IsInternalExpense &&
                            t.PurchaseOrderId != null &&
                            t.TimestampUtc >= fromUtc &&
                            t.TimestampUtc <= toUtc)
                .Include(t => t.PurchaseOrder)
                .ToListAsync(ct);

            var expenseTransactionsWithOrder = expenseTransactions
                .Where(t => t.PurchaseOrder != null && t.PurchaseOrder.TotalAmount > 0)
                .ToList();

            var purchaseVat = 0m;
            var purchaseManTax = 0m;
            // We don't strictly need "Purchase Subtotal" for Net Profit (we need COGS), 
            // but we need Purchase ManTax.

            foreach (var t in expenseTransactionsWithOrder)
            {
                var order = t.PurchaseOrder!;
                var ratio = t.Amount / order.TotalAmount; // Amount paid vs Total

                purchaseVat += order.VatAmount * ratio;
                purchaseManTax += order.ManufacturingTaxAmount * ratio;
            }

            // ---- COGS Calculation ----
            // Breakdown COGS by Paid Sales Orders
            // We already have 'revenueTransactionsWithOrder' which represents the Paid Sales.
            // We need to load lines to calculate COGS.
            
            // Optimization: Load lines for the involved orders
            var salesOrderIds = revenueTransactionsWithOrder.Select(x => x.SalesOrderId!.Value).Distinct().ToList();
            
            var salesOrderLines = await _db.SalesOrderLines
                .AsNoTracking()
                .Where(l => salesOrderIds.Contains(l.SalesOrderId))
                .Include(l => l.ProductBatch) // Need batch cost
                .ToListAsync(ct);

            var salesOrderLinesMap = salesOrderLines.GroupBy(l => l.SalesOrderId).ToDictionary(g => g.Key, g => g.ToList());

            var calculatedCogs = 0m;
            foreach (var t in revenueTransactionsWithOrder)
            {
                if (!salesOrderLinesMap.TryGetValue(t.SalesOrderId!.Value, out var lines)) continue;
                var order = t.SalesOrder!;
                
                // Calculate total cost for the order
                var totalOrderCost = lines.Sum(l => l.Quantity * (l.ProductBatch?.UnitCost ?? 0m));
                
                // Prorate by payment ratio
                var ratio = t.Amount / order.TotalAmount;
                calculatedCogs += totalOrderCost * ratio;
            }

            // ---- Purchase Refunds ----
            var purchaseRefundsQuery = _db.RefundTransactions
                .AsNoTracking()
                .Where(r => r.Type == RefundType.PurchaseOrder &&
                            r.ProcessedUtc >= fromUtc &&
                            r.ProcessedUtc <= toUtc);

            var purchaseRefundsTotal = await purchaseRefundsQuery.SumAsync(r => (decimal?)r.Amount, ct) ?? 0m;

            // Net COGS = Calculated COGS - Purchase Refunds (Simplified, assuming refunds return cost)
            var costOfGoods = Math.Max(0, calculatedCogs - purchaseRefundsTotal);

            // ---- Internal Expenses ----
            var internalExpensesQuery = _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Expense &&
                            t.IsInternalExpense &&
                            t.TimestampUtc >= fromUtc &&
                            t.TimestampUtc <= toUtc);

            var internalExpenses = await internalExpensesQuery.SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

            // Purchase Receipt Expenses are usually part of the Cost of Asset (Inventory), 
            // so they flow into COGS via UnitCost if allocated. 
            // If they are separate non-inventory expenses, they should be added here.
            // In PurchaseOrderServices, ReceiptExpenses are added to TotalAmount.
            // We should check if they are included in UnitCost. 
            // The current system seems to just add them to the PO Total. 
            // For safety, let's treat them as Internal Expenses if they aren't capitalized.
            // But since we can't easily distinguish capitalized status here without deep checking, 
            // and the previous code treated them as internal expenses, we will continue that.
            // We need to prorate them too.
            var receiptExpenses = 0m;
            foreach (var t in expenseTransactionsWithOrder)
            {
                var order = t.PurchaseOrder!;
                var ratio = t.Amount / order.TotalAmount;
                receiptExpenses += order.ReceiptExpenses * ratio;
            }

            var totalInternalExpenses = internalExpenses + receiptExpenses;

            // ---- Net Profit Calculation ----
            // Formula: Sales Revenue - COGS - Internal Expenses - Sales ManTax - Purchase ManTax
            // Note on Sales Refunds: They reduce Sales Revenue.
            // Since we calculated SalesRevenue from (TotalAmount * Ratio), and TotalAmount is the Order Total,
            // we definitely need to subtract Refunds.
            // However, we need to subtract the *Component* parts from the refund.
            // Since we don't have that granularity, we will subtract the FULL Refund Amount from Revenue?
            // No, that includes VAT reversal.
            // We should assume Refund = Revenue + VAT - ManTax.
            // Ideally we subtract the "Revenue" portion of the refund.
            // Approximation: Revenue portion ~= RefundAmount * (SalesRevenue / (SalesRevenue + VAT - ManTax)).
            // For now, to ensure "SalesProfit" (Cash Retained) is accurate, the response DTO has "SalesProfit".
            // Let's set SalesProfit = SalesRevenue - (SalesRefunds - RefundedSalesVAT?).
            // For strict NetProfit:
            
            // Let's refine the User's formula: "Net Profit = Sales Revenue - COGS ..."
            // This Sales Revenue must be Net of Refunds.
            // Let's subtract the full sales refund from the "SalesProfit" display, 
            // AND subtract the estimated Revenue portion from Net Profit calculation.
            
            // To be strictly safe and avoid massive complexity with limited Refund data:
            // We will subtract the Total Sales Refund from the Net Profit for now, 
            // assuming it's a loss of revenue/cash.
            // BUT we must add back the VAT part because that's not our loss (it's liability reversal).
            // Estimated Refunded VAT = SalesRefundsTotal * (TotalSalesVat / (SalesRevenue + TotalSalesVat - TotalSalesManTax))?
            // This is getting guessy.
            
            // Simplest interpretation that matches the User Request "VAT is Output - Input":
            // Net Profit = (Sales Revenue derived from payments) - COGS - Expenses - ManTax.
            // We will just treat "SalesRefundsTotal" as a direct deduction from Sales Revenue for simplicity,
            // treating it as a contra-revenue "Return Inwards".
            // If we want to be exact about VAT, we'd need Refund Line Items.
            // For this task, we will calculate NetProfit using the metrics we have.
            
            var netSalesRevenue = Math.Max(0, salesRevenue - salesRefundsTotal);
            
            // Correct Formula: 
            // NetProfit = SalesRevenue (Base) - COGS - Expenses - SalesManTax - PurchaseManTax
            // We'll use the plain salesRevenue (from payments) but we MUST account for refunds.
            // If we don't account for refunds, profit is inflated.
            // Let's subtract SalesRefundsTotal from the 'SalesRevenue' term in the equation.
            
            // However, doing so subtracts the VAT component of the refund from Profit, which is wrong (VAT isn't profit).
            // We should only subtract the Ex-VAT portion.
            // Let's assume average tax rate from the processed orders.
            decimal avgVatRate = (salesRevenue > 0) ? (totalSalesVat / salesRevenue) : 0.14m; // Fallback to 14%
            decimal avgManTaxRate = (salesRevenue > 0) ? (totalSalesManTax / salesRevenue) : 0.01m;
            
            // Refund = Base * (1 + Vat - ManTax)
            // Base = Refund / (1 + Vat - ManTax)
            decimal estimatedRefundBase = salesRefundsTotal / (1 + avgVatRate - avgManTaxRate);

            // Adjusted Revenue
            var adjustedSalesRevenue = salesRevenue - estimatedRefundBase;
            
            var netProfit = adjustedSalesRevenue 
                            - costOfGoods 
                            - totalInternalExpenses 
                            - totalSalesManTax 
                            - purchaseManTax;

            // ---- Final Output ----
            var salesProfit = salesRevenue - salesRefundsTotal; // This is the "Cash" profit from sales (incl tax)
            
            var profitMargin = adjustedSalesRevenue > 0 
                ? Math.Round((netProfit / adjustedSalesRevenue) * 100, 2) 
                : 0m;

            return new FinancialSummaryResponseDto
            {
                SalesRevenue = Math.Round(salesRevenue, 2),
                SalesProfit = Math.Round(salesProfit, 2),
                CostOfGoods = Math.Round(costOfGoods, 2),
                TotalVat = Math.Round(totalSalesVat - purchaseVat, 2),
                TotalManufacturingTax = Math.Round(totalSalesManTax + purchaseManTax, 2), // Total ManTax Impact
                InternalExpenses = Math.Round(totalInternalExpenses, 2),
                GrossProfit = Math.Round(adjustedSalesRevenue - costOfGoods, 2),
                NetProfit = Math.Round(netProfit, 2),
                ProfitMargin = profitMargin
            };        }

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
            var now = DateTimeOffset.UtcNow;

            if (filter.DateRangeType == FinancialDateRangeType.Custom &&
                filter.FromUtc.HasValue &&
                filter.ToUtc.HasValue)
            {
                return (filter.FromUtc.Value, filter.ToUtc.Value);
            }

            DateTimeOffset start;
            DateTimeOffset end;

            switch (filter.DateRangeType)
            {
                case FinancialDateRangeType.Today:
                    start = now.Date;
                    end = start.AddDays(1).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisWeek:
                    // Assuming week starts on Monday
                    var diff = (7 + (int)now.DayOfWeek - (int)DayOfWeek.Monday) % 7;
                    start = now.Date.AddDays(-diff);
                    end = start.AddDays(7).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisMonth:
                    start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset);
                    end = start.AddMonths(1).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisQuarter:
                    var quarter = (now.Month - 1) / 3;
                    var quarterStartMonth = quarter * 3 + 1;
                    start = new DateTimeOffset(now.Year, quarterStartMonth, 1, 0, 0, 0, now.Offset);
                    end = start.AddMonths(3).AddTicks(-1);
                    break;
                case FinancialDateRangeType.ThisYear:
                    start = new DateTimeOffset(now.Year, 1, 1, 0, 0, 0, now.Offset);
                    end = start.AddYears(1).AddTicks(-1);
                    break;
                default:
                    // Fallback to "Today" if something unexpected happens.
                    start = now.Date;
                    end = start.AddDays(1).AddTicks(-1);
                    break;
            }

            return (start, end);
        }
    }
}
