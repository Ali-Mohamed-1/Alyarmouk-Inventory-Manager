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
            var supplier = await _db.Suppliers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == supplierId, ct);

            if (supplier is null)
            {
                throw new InvalidOperationException($"Supplier with id {supplierId} was not found.");
            }

            var totalOrders = await _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.SupplierId == supplierId && po.Status != PurchaseOrderStatus.Cancelled)
                .SumAsync(po => (decimal?)po.TotalAmount, ct) ?? 0m;

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

            // ---- Sales side (revenue + output taxes) ----
            var salesQuery = _db.SalesOrders
                .AsNoTracking()
                .Where(o => o.Status == SalesOrderStatus.Completed &&
                            o.CreatedUtc >= fromUtc &&
                            o.CreatedUtc <= toUtc);

            var totalSales = await salesQuery.SumAsync(o => (decimal?)o.Subtotal, ct) ?? 0m;
            var totalSalesVat = await salesQuery.SumAsync(o => (decimal?)o.VatAmount, ct) ?? 0m;
            var totalSalesManufacturingTax = await salesQuery.SumAsync(o => (decimal?)o.ManufacturingTaxAmount, ct) ?? 0m;

            // ---- Purchase side (cost of goods + input taxes + receipt expenses) ----
            var purchaseQuery = _db.PurchaseOrders
                .AsNoTracking()
                .Where(po => po.Status == PurchaseOrderStatus.Received &&
                             po.CreatedUtc >= fromUtc &&
                             po.CreatedUtc <= toUtc);

            // Cost of goods excludes receipt expenses – those are counted as internal expenses.
            var costOfGoods = await purchaseQuery.SumAsync(po => (decimal?)po.Subtotal, ct) ?? 0m;
            var purchaseVat = await purchaseQuery.SumAsync(po => (decimal?)po.VatAmount, ct) ?? 0m;
            var purchaseManufacturingTax = await purchaseQuery.SumAsync(po => (decimal?)po.ManufacturingTaxAmount, ct) ?? 0m;
            var receiptExpenses = await purchaseQuery.SumAsync(po => (decimal?)po.ReceiptExpenses, ct) ?? 0m;

            // ---- Internal expenses (FinancialTransaction) ----
            var internalExpensesQuery = _db.FinancialTransactions
                .AsNoTracking()
                .Where(t => t.Type == FinancialTransactionType.Expense &&
                            t.IsInternalExpense &&
                            t.TimestampUtc >= fromUtc &&
                            t.TimestampUtc <= toUtc);

            var internalExpenses = await internalExpensesQuery.SumAsync(t => (decimal?)t.Amount, ct) ?? 0m;

            // Internal expenses metric combines explicitly tracked internal expenses
            // and purchase receipt expenses.
            var totalInternalExpenses = internalExpenses + receiptExpenses;

            var grossProfit = totalSales - costOfGoods;
            var netProfit = grossProfit - totalInternalExpenses;

            return new FinancialSummaryResponseDto
            {
                TotalSales = totalSales,
                CostOfGoods = costOfGoods,
                TotalVat = totalSalesVat - purchaseVat,
                TotalManufacturingTax = totalSalesManufacturingTax - purchaseManufacturingTax,
                InternalExpenses = totalInternalExpenses,
                GrossProfit = grossProfit,
                NetProfit = netProfit
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
