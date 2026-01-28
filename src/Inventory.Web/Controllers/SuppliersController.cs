using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Supplier;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class SuppliersController : Controller
{
    private readonly ISupplierServices _suppliers;

    public SuppliersController(ISupplierServices suppliers)
    {
        _suppliers = suppliers;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _suppliers.GetAllAsync(cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var supplier = await _suppliers.GetByIdAsync(id, cancellationToken);
        if (supplier is null)
        {
            return NotFound();
        }

        return View(supplier);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateSupplierRequest("", "", "", ""));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateSupplierRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = GetUserContext();
        var id = await _suppliers.CreateAsync(model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var existing = await _suppliers.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var model = new UpdateSupplierRequest(
            existing.Id,
            existing.Name,
            existing.Phone,
            existing.Email,
            existing.Address,
            existing.IsActive);

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UpdateSupplierRequest model, CancellationToken cancellationToken)
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
        await _suppliers.UpdateAsync(id, model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        var user = GetUserContext();
        await _suppliers.SetActiveAsync(id, isActive, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
