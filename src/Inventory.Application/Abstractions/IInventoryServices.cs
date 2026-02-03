using Inventory.Application.DTOs;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Application.DTOs.Transaction;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.PurchaseOrder;

namespace Inventory.Application.Abstractions
{
    public interface IInventoryServices
    {
        /// <summary>
        /// Returns the on-hand quantity for a product right now
        /// </summary>
        Task<decimal> GetOnHandAsync(int productId, CancellationToken ct = default);

        /// <summary>
        /// Retrieves the latest stock snapshot for a specific product
        /// </summary>
        Task<StockSnapshotResponseDto?> GetStockAsync(int productId, CancellationToken ct = default);

        /// <summary>
        /// Lists stock snapshots for all products
        /// </summary>
        Task<IReadOnlyList<StockSnapshotResponseDto>> GetAllStockAsync(CancellationToken ct = default);

        /// <summary>
        /// Records incoming stock and attributes it to the requesting user
        /// </summary>
        Task ReceiveAsync(StockReceiveRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Records outgoing stock and attributes it to the requesting user
        /// </summary>
        Task IssueAsync(StockIssueRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Adjusts stock counts for a product, capturing who made the change
        /// </summary>
        Task UpdateStockAsync(UpdateStockRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Creates a detailed inventory transaction entry for auditing and history
        /// </summary>
        Task<long> CreateTransactionAsync(CreateInventoryTransactionRequest req, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Gets the latest inventory transactions
        /// </summary>
        Task<IReadOnlyList<InventoryTransactionResponseDto>> GetRecentTransactionsAsync(int take = 50, CancellationToken ct = default);

        /// <summary>
        /// Retrieves all transactions associated with a specific product
        /// </summary>
        Task<IReadOnlyList<InventoryTransactionResponseDto>> GetProductTransactionsAsync(int productId, CancellationToken ct = default);

        /// <summary>
        /// Processes stock receipt for a Purchase Order (PurchaseOrder.Status = Received)
        /// </summary>
        Task ProcessPurchaseOrderStockAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Reverses stock receipt for a Purchase Order (e.g. Received -> Cancelled)
        /// </summary>
        Task ReversePurchaseOrderStockAsync(long purchaseOrderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Refunds stock for a Purchase Order (e.g. partial refund)
        /// </summary>
        Task RefundPurchaseOrderStockAsync(long purchaseOrderId, List<RefundPurchaseLineItem> lines, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Processes stock issue for a Sales Order (SalesOrder.Status = Done)
        /// </summary>
        Task ProcessSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Reverses stock issue for a Sales Order (e.g. Done -> Cancelled)
        /// </summary>
        Task ReverseSalesOrderStockAsync(long salesOrderId, UserContext user, CancellationToken ct = default);

        /// <summary>
        /// Refunds stock for a Sales Order (e.g. partial refund)
        /// </summary>
        Task RefundSalesOrderStockAsync(long salesOrderId, List<RefundLineItem> lines, UserContext user, CancellationToken ct = default);
    }
}
