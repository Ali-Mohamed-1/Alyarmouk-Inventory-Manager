using Inventory.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Web.Controllers
{
    public class CleanupController : Controller
    {
        private readonly AppDbContext _context;

        public CleanupController(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                // Delete in order to satisfy foreign key constraints
                
                // 1. Financial Transactions
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM FinancialTransactions");
                
                // 2. Refund Lines and Transactions
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM RefundTransactionLines");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM RefundTransactions");
                
                // 3. Payment Records
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM PaymentRecords");
                
                // 4. Order Lines
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM SalesOrderLines");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM PurchaseOrderLines");
                
                // 5. Inventory Transactions
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM InventoryTransactions");
                
                // 6. Orders
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM SalesOrders");
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM PurchaseOrders");
                
                // 7. Stock Snapshots (Can be recalculated, but safe to clear)
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM StockSnapshots");

                // Reset Product Batch quantities (optional but usually desired when clearing transactions)
                await _context.Database.ExecuteSqlRawAsync("UPDATE ProductBatches SET OnHand = 0, Reserved = 0");

                await _context.SaveChangesAsync();

                return Ok("Database cleanup successful. Sales, Purchases, and Transactions have been removed. Products, Batches, Identity, Customers, and Suppliers were preserved.");
            }
            catch (Exception ex)
            {
                return BadRequest($"Cleanup failed: {ex.Message}");
            }
        }
    }
}
