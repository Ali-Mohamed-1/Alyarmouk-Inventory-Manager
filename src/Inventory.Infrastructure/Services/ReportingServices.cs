using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs.Reporting;
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
    }
}
