using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/purchase-orders")]
[ApiController]
public class PurchaseOrdersApiController : ControllerBase
{
    private readonly IPurchaseOrderServices _purchaseOrderServices;

    public PurchaseOrdersApiController(IPurchaseOrderServices purchaseOrderServices)
    {
        _purchaseOrderServices = purchaseOrderServices;
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PurchaseOrderResponse>> GetById(int id, CancellationToken ct)
    {
        var order = await _purchaseOrderServices.GetByIdAsync(id, ct);
        if (order is null)
        {
            return NotFound();
        }
        return Ok(order);
    }
}
