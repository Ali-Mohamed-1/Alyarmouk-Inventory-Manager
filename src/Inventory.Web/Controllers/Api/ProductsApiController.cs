using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inventory.Application.Abstractions;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/products")]
[ApiController]
public sealed class ProductsApiController : ControllerBase
{
    private readonly IProductServices _products;
    private readonly IInventoryServices _inventory;

    public ProductsApiController(IProductServices products, IInventoryServices inventory)
    {
        _products = products;
        _inventory = inventory;
    }

    /// <summary>
    /// Returns products with their current stock snapshot and a derived status.
    /// </summary>
    [HttpGet("with-stock")]
    public async Task<IActionResult> GetWithStock(CancellationToken cancellationToken)
    {
        var products = await _products.GetAllAsync(cancellationToken);
        var stock = await _inventory.GetAllStockAsync(cancellationToken);

        var stockByProductId = stock.ToDictionary(x => x.ProductId);

        var result = products.Select(p =>
        {
            stockByProductId.TryGetValue(p.Id, out var s);
            var onHand = s?.OnHand ?? 0m;
            var reserved = s?.Reserved ?? 0m;
            var available = s?.Available ?? (onHand - reserved);
            var status = "InStock";
            if (onHand == 0)
            {
                status = "NoStock";
            }
            else if (available <= p.ReorderPoint)
            {
                status = "LowStock";
            }

            return new
            {
                productId = p.Id,
                sku = p.Sku,
                name = p.Name,
                unit = p.Unit,

                reorderPoint = p.ReorderPoint,
                onHand,
                reserved,
                available,
                status,
                rowVersion = p.RowVersion, // Product version for edits
                stockRowVersion = s?.RowVersion ?? string.Empty // Stock version for adjustments
            };
        });

        return Ok(result);
    }
}

