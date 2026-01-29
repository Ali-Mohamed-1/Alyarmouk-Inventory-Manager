using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.Product;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class ProductsController : Controller
{
    private readonly IProductServices _products;
    private readonly ICategoryServices _categories;

    public ProductsController(IProductServices products, ICategoryServices categories)
    {
        _products = products;
        _categories = categories;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _products.GetAllAsync(cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        return View(product);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        ViewBag.Categories = await _categories.GetAllAsync(cancellationToken);
        return View(new CreateProductRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProductRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _categories.GetAllAsync(cancellationToken);
            return View(model);
        }

        var user = GetUserContext();
        await _products.CreateAsync(model, user, cancellationToken);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var existing = await _products.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var model = new UpdateProductRequest
        {
            Id = existing.Id,
            Sku = existing.Sku,
            Name = existing.Name,
            CategoryId = existing.CategoryId,
            Unit = existing.Unit,
            Price = existing.Price,
            ReorderPoint = existing.ReorderPoint,
            IsActive = existing.IsActive,
            RowVersion = existing.RowVersion
        };

        ViewBag.Categories = await _categories.GetAllAsync(cancellationToken);
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UpdateProductRequest model, CancellationToken cancellationToken)
    {
        if (id != model.Id)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Categories = await _categories.GetAllAsync(cancellationToken);
            return View(model);
        }

        var user = GetUserContext();
        await _products.UpdateAsync(id, model, user, cancellationToken);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool isActive, CancellationToken cancellationToken)
    {
        var user = GetUserContext();
        await _products.SetActiveAsync(id, isActive, user, cancellationToken);
        return RedirectToAction("Index", "Dashboard");
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}

