using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Customer;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class CustomersController : Controller
{
    private readonly ICustomerServices _customers;

    public CustomersController(ICustomerServices customers)
    {
        _customers = customers;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _customers.GetAllAsync(cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return NotFound();
        }

        return View(customer);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateCustomerRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateCustomerRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = GetUserContext();
        var id = await _customers.CreateAsync(model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var existing = await _customers.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var model = new UpdateCustomerRequest
        {
            Id = existing.Id,
            Name = existing.Name,
            Email = existing.Email,
            Phone = existing.Phone,
            Address = existing.Address
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UpdateCustomerRequest model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = GetUserContext();
        await _customers.UpdateAsync(id, model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}

