using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Application.DTOs.Transaction;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/activity")]
[ApiController]
public class ActivityApiController : ControllerBase
{
    private readonly ISalesOrderServices _salesOrderServices;
    private readonly IPurchaseOrderServices _purchaseOrderServices;
    private readonly IInventoryTransactionServices _inventoryTransactionServices;

    public ActivityApiController(
        ISalesOrderServices salesOrderServices,
        IPurchaseOrderServices purchaseOrderServices,
        IInventoryTransactionServices inventoryTransactionServices)
    {
        _salesOrderServices = salesOrderServices;
        _purchaseOrderServices = purchaseOrderServices;
        _inventoryTransactionServices = inventoryTransactionServices;
    }

    [HttpGet("sales-orders")]
    public async Task<ActionResult<IEnumerable<SalesOrderResponseDto>>> GetRecentSalesOrders(CancellationToken ct)
    {
        // Fetching recent 50 orders
        var orders = await _salesOrderServices.GetRecentAsync(50, ct);
        return Ok(orders);
    }

    [HttpGet("purchase-orders")]
    public async Task<ActionResult<IEnumerable<PurchaseOrderResponse>>> GetRecentPurchaseOrders(CancellationToken ct)
    {
        // Fetching recent 50 orders
        var orders = await _purchaseOrderServices.GetRecentAsync(50, ct);
        return Ok(orders);
    }

    [HttpGet("inventory-transactions")]
    public async Task<ActionResult<IEnumerable<InventoryTransactionResponseDto>>> GetRecentInventoryTransactions(CancellationToken ct)
    {
        // Fetching recent 50 transactions
        var transactions = await _inventoryTransactionServices.GetRecentAsync(50, ct);
        return Ok(transactions);
    }
}
