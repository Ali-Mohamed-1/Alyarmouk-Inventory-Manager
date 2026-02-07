using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class CategoriesController : Controller
{
    private readonly ICategoryServices _categories;

    public CategoriesController(ICategoryServices categories)
    {
        _categories = categories;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var items = await _categories.GetAllAsync(cancellationToken);
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken)
    {
        var category = await _categories.GetByIdAsync(id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        return View(category);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new CreateCategoryRequest());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CreateCategoryRequest model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = GetUserContext();
        var id = await _categories.CreateAsync(model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var existing = await _categories.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return NotFound();
        }

        var model = new UpdateCategoryRequest
        {
            Id = existing.Id,
            Name = existing.Name,
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, UpdateCategoryRequest model, CancellationToken cancellationToken)
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
        await _categories.UpdateAsync(id, model, user, cancellationToken);
        return RedirectToAction(nameof(Details), new { id });
    }

    private UserContext GetUserContext()
    {
        // For now we build a simple user context from the current principal.
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}

