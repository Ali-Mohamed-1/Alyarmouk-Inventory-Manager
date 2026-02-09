using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.PurchaseOrder;
using Inventory.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class PurchaseOrdersController : Controller
{
    private readonly IPurchaseOrderServices _orders;
    private readonly ISupplierServices _suppliers;
    private readonly IProductServices _products;

    public PurchaseOrdersController(
        IPurchaseOrderServices orders,
        ISupplierServices suppliers,
        IProductServices products)
    {
        _orders = orders;
        _suppliers = suppliers;
        _products = products;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _orders.GetRecentAsync(ct: cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Details(long id, CancellationToken cancellationToken)
    {
        var order = await _orders.GetByIdAsync(id, cancellationToken);
        if (order is null)
        {
            return NotFound();
        }

        return View(order);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewBag.Suppliers = await _suppliers.GetForDropdownAsync(cancellationToken);
        ViewBag.Products = await _products.GetAllAsync(cancellationToken);
        return View(new CreatePurchaseOrderRequest());
    }

    [HttpGet]
    public async Task<IActionResult> CreateHistorical(CancellationToken cancellationToken)
    {
        ViewBag.Suppliers = await _suppliers.GetForDropdownAsync(cancellationToken);
        ViewBag.Products = await _products.GetAllAsync(cancellationToken);
        return View(new CreatePurchaseOrderRequest 
        { 
            IsHistorical = true, 
            Status = Inventory.Domain.Entities.PurchaseOrderStatus.Received,
            ConnectToReceiveStock = false // Explicitly false, though CreateAsync ignores it for historical if logic is correct
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ActivateStock(long id, CancellationToken cancellationToken)
    {
        var user = GetUserContext();
        await _orders.ActivateStockAsync(id, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreatePurchaseOrderRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Suppliers = await _suppliers.GetForDropdownAsync(cancellationToken);
            ViewBag.Products = await _products.GetAllAsync(cancellationToken);
            return model.IsHistorical ? View("CreateHistorical", model) : View(model);
        }

        var user = GetUserContext();
        var id = await _orders.CreateAsync(model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(long id, PurchaseOrderStatus status, CancellationToken cancellationToken)
    {
        var user = GetUserContext();
        await _orders.UpdateStatusAsync(id, status, user, ct: cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(long id, CancellationToken cancellationToken)
    {
        var user = GetUserContext();
        await _orders.CancelAsync(id, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
