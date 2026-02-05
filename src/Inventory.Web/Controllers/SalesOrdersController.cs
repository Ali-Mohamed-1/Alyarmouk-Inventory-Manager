using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SalesOrder;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class SalesOrdersController : Controller
{
    private readonly ISalesOrderServices _orders;
    private readonly ICustomerServices _customers;

    public SalesOrdersController(ISalesOrderServices orders, ICustomerServices customers)
    {
        _orders = orders;
        _customers = customers;
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
        ViewBag.Customers = await _customers.GetForDropdownAsync(cancellationToken);
        return View(new CreateSalesOrderRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateSalesOrderRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Customers = await _customers.GetForDropdownAsync(cancellationToken);
            return View(model);
        }

        var user = GetUserContext();
        var id = await _orders.CreateAsync(model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(long id, UpdateSalesOrderStatusRequest model, CancellationToken cancellationToken)
    {
        if (id != model.OrderId)
        {
            return BadRequest();
        }

        var user = GetUserContext();
        await _orders.UpdateStatusAsync(id, model, user, cancellationToken);
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

    [HttpGet]
    public async Task<IActionResult> CustomerHistory(int customerId, CancellationToken cancellationToken)
    {
        var orders = await _orders.GetCustomerOrdersAsync(customerId, ct: cancellationToken);
        ViewBag.CustomerId = customerId;
        return View(orders);
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}

