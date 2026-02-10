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
    
    public ProductsController(IProductServices products)
    {
        _products = products;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Dashboard");
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
        return View(new CreateProductRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateProductRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
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
            Unit = existing.Unit,

            ReorderPoint = existing.ReorderPoint,
            IsActive = existing.IsActive,
            RowVersion = existing.RowVersion
        };

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

    [HttpGet]
    public async Task<IActionResult> AddBatch(int id, CancellationToken cancellationToken)
    {
        var product = await _products.GetByIdAsync(id, cancellationToken);
        if (product is null)
        {
            return NotFound();
        }

        var model = new CreateBatchRequest
        {
            ProductId = id
        };

        ViewBag.ProductName = product.Name;
        ViewBag.ProductSku = product.Sku;
        
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddBatch(int id, CreateBatchRequest model, CancellationToken cancellationToken)
    {
        if (id != model.ProductId)
        {
            return BadRequest();
        }

        if (!ModelState.IsValid)
        {
            var product = await _products.GetByIdAsync(id, cancellationToken);
            if (product != null) 
            {
                ViewBag.ProductName = product.Name;
                ViewBag.ProductSku = product.Sku;
            }
            return View(model);
        }

        var user = GetUserContext();
        await _products.AddBatchAsync(model, user, cancellationToken);
        
        return RedirectToAction("Index", "Dashboard");
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}

