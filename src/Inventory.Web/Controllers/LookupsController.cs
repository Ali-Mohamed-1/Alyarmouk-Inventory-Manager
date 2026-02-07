using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

// Lightweight JSON endpoints used for dropdowns/autocomplete in the web UI
public sealed class LookupsController : Controller
{
    private readonly ICategoryServices _categories;
    private readonly IProductServices _products;
    private readonly ICustomerServices _customers;
    private readonly ISupplierServices _suppliers;
    private readonly ISalesOrderServices _salesOrders;

    public LookupsController(
        ICategoryServices categories,
        IProductServices products,
        ICustomerServices customers,
        ISupplierServices suppliers,
        ISalesOrderServices salesOrders)
    {
        _categories = categories;
        _products = products;
        _customers = customers;
        _suppliers = suppliers;
        _salesOrders = salesOrders;
    }

    [HttpGet]
    public async Task<IActionResult> Categories(CancellationToken cancellationToken)
    {
        var items = await _categories.GetAllAsync(cancellationToken);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> Products(CancellationToken cancellationToken)
    {
        var items = await _products.GetAllAsync(cancellationToken);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> Customers(string? q, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(q))
        {
            var results = await _customers.SearchByNameAsync(q, ct: cancellationToken);
            return Json(results);
        }

        var items = await _customers.GetForDropdownAsync(cancellationToken);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> RecentSalesOrders(CancellationToken cancellationToken)
    {
        var items = await _salesOrders.GetRecentAsync(ct: cancellationToken);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> Suppliers(CancellationToken cancellationToken)
    {
        var items = await _suppliers.GetForDropdownAsync(cancellationToken);
        return Json(items);
    }

    [HttpGet]
    public async Task<IActionResult> SupplierProducts(int supplierId, CancellationToken cancellationToken)
    {
        var items = await _suppliers.GetSupplierProductsAsync(supplierId, cancellationToken);
        return Json(items);
    }
}

