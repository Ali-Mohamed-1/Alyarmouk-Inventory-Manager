using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;



namespace Inventory.Web.Controllers;

public sealed class DashboardController : Controller
{
    private readonly IReportingServices _reporting;
    private readonly ISalesOrderServices _salesOrders;
    private readonly IPurchaseOrderServices _purchaseOrders;

    public DashboardController(
        IReportingServices reporting,
        ISalesOrderServices salesOrders,
        IPurchaseOrderServices purchaseOrders)
    {
        _reporting = reporting;
        _salesOrders = salesOrders;
        _purchaseOrders = purchaseOrders;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await _reporting.GetDashboardAsync(cancellationToken);
        return View(dashboard);
    }

    /// <summary>
    /// JSON endpoint used by the dashboard SPA to render low stock table.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> LowStockJson(CancellationToken cancellationToken)
    {
        var items = await _reporting.GetLowStockAsync(cancellationToken);
        return Json(items);
    }

    /// <summary>
    /// JSON endpoint for dashboard summary metrics including order counts.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SummaryJson(CancellationToken cancellationToken)
    {
        var dashboard = await _reporting.GetDashboardAsync(cancellationToken);

        return Json(new
        {
            totalProducts = dashboard.TotalProducts,
            totalSalesOrders = dashboard.TotalSalesOrders,
            totalPurchaseOrders = dashboard.TotalPurchaseOrders,
            lowStockCount = dashboard.LowStockCount,
            totalOnHand = dashboard.TotalOnHand
        });
    }

}


