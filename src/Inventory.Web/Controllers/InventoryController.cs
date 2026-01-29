using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.StockSnapshot;
using Inventory.Application.DTOs.Transaction;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class InventoryController : Controller
{
    private readonly IInventoryServices _inventory;

    public InventoryController(IInventoryServices inventory)
    {
        _inventory = inventory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var stock = await _inventory.GetAllStockAsync(cancellationToken);
        return View(stock);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int productId, CancellationToken cancellationToken)
    {
        var snapshot = await _inventory.GetStockAsync(productId, cancellationToken);
        if (snapshot is null)
        {
            return NotFound();
        }

        return View(snapshot);
    }

    [HttpGet]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        ViewBag.Products = await _inventory.GetAllStockAsync(cancellationToken);
        return View(new StockReceiveRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(StockReceiveRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = GetUserContext();
        await _inventory.ReceiveAsync(model, user, cancellationToken);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public async Task<IActionResult> Issue(CancellationToken cancellationToken)
    {
        ViewBag.Products = await _inventory.GetAllStockAsync(cancellationToken);
        return View(new StockIssueRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(StockIssueRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Products = await _inventory.GetAllStockAsync(cancellationToken);
            return View(model);
        }

        var user = GetUserContext();
        await _inventory.IssueAsync(model, user, cancellationToken);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public IActionResult Adjust()
    {
        return View(new UpdateStockRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(UpdateStockRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = GetUserContext();
        await _inventory.UpdateStockAsync(model, user, cancellationToken);
        return RedirectToAction("Index", "Dashboard");
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}

