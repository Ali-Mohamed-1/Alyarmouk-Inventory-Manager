using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Transaction;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class InventoryTransactionsController : Controller
{
    private readonly IInventoryTransactionServices _transactions;

    public InventoryTransactionsController(IInventoryTransactionServices transactions)
    {
        _transactions = transactions;
    }

    [HttpGet]
    public async Task<IActionResult> Index(int take = 50, CancellationToken cancellationToken = default)
    {
        var items = await _transactions.GetRecentAsync(take, cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ByProduct(int productId, CancellationToken cancellationToken)
    {
        var items = await _transactions.GetByProductAsync(productId, cancellationToken);
        ViewBag.ProductId = productId;
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> ByCustomer(int customerId, CancellationToken cancellationToken)
    {
        var items = await _transactions.GetTransactionsByCustomerAsync(customerId, ct: cancellationToken);
        ViewBag.CustomerId = customerId;
        return View(items);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateInventoryTransactionRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateInventoryTransactionRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = GetUserContext();
        await _transactions.CreateAsync(model, user, cancellationToken);
        return RedirectToAction(nameof(Index));
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}

