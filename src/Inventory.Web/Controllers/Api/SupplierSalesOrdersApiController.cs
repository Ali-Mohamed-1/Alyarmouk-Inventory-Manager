using Inventory.Application.Abstractions;
using Inventory.Application.DTOs;
using Inventory.Application.DTOs.SupplierSalesOrder;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers.Api;

[Route("api/supplier-sales-orders")]
[ApiController]
public class SupplierSalesOrdersApiController : ControllerBase
{
    private readonly ISupplierSalesOrderServices _services;

    public SupplierSalesOrdersApiController(ISupplierSalesOrderServices services)
    {
        _services = services;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SupplierSalesOrderResponseDto>>> GetRecent(CancellationToken ct)
    {
        var orders = await _services.GetRecentAsync(ct: ct);
        return Ok(orders);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SupplierSalesOrderResponseDto>> GetById(long id, CancellationToken ct)
    {
        var order = await _services.GetByIdAsync(id, ct);
        if (order is null) return NotFound();
        return Ok(order);
    }

    [HttpPost("{id}/cancel")]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        var user = GetUserContext();
        await _services.CancelAsync(id, user, ct);
        return Ok();
    }

    private UserContext GetUserContext()
    {
        var userId = User?.Identity?.Name ?? "system";
        var displayName = User?.Identity?.Name ?? "System";
        return new UserContext(userId, displayName);
    }
}
