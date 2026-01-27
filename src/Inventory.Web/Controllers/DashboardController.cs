using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class DashboardController : Controller
{
    private readonly IReportingServices _reporting;

    public DashboardController(IReportingServices reporting)
    {
        _reporting = reporting;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var dashboard = await _reporting.GetDashboardAsync(cancellationToken);
        return View(dashboard);
    }

    [HttpGet]
    public async Task<IActionResult> LowStock(CancellationToken cancellationToken)
    {
        var items = await _reporting.GetLowStockAsync(cancellationToken);
        return View(items);
    }
}

