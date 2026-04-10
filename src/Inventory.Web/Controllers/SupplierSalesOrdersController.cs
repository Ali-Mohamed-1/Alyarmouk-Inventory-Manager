using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SupplierSalesOrder;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class SupplierSalesOrdersController : Controller
{
    private readonly ISupplierSalesOrderServices _orders;
    private readonly ISupplierServices _suppliers;
    private readonly IProductServices _products;

    public SupplierSalesOrdersController(
        ISupplierSalesOrderServices orders,
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
    public async Task<IActionResult> Create(int? supplierId, CancellationToken cancellationToken)
    {
        ViewBag.Suppliers = await _suppliers.GetForDropdownAsync(cancellationToken);
        ViewBag.Products = await _products.GetAllAsync(cancellationToken);
        
        var now = DateTimeOffset.UtcNow;
        return View(new CreateSupplierSalesOrderRequest
        { 
            SupplierId = supplierId ?? 0,
            OrderDate = now,
            DueDate = now.AddMonths(1)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateSupplierSalesOrderRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Suppliers = await _suppliers.GetForDropdownAsync(cancellationToken);
            ViewBag.Products = await _products.GetAllAsync(cancellationToken);
            return View(model);
        }

        var user = GetUserContext();
        var id = await _orders.CreateAsync(model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
